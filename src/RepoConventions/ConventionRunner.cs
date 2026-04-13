using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoConventions;

internal sealed class ConventionRunner
{
	public ConventionRunner(string targetRepositoryRoot, GitClient targetGitClient, TextWriter standardOutput, TextWriter standardError, Func<string, string, string>? remoteRepositoryUrlResolver, CancellationToken cancellationToken)
	{
		m_targetRepositoryRoot = targetRepositoryRoot;
		m_targetGitClient = targetGitClient;
		m_standardOutput = standardOutput;
		m_standardError = standardError;
		m_cancellationToken = cancellationToken;
		m_remoteRepositoryUrlResolver = remoteRepositoryUrlResolver;
	}

	public async Task<int> RunAsync(string topLevelConfigPath, bool openPr)
	{
		PullRequestPreparation? pullRequest = null;
		if (openPr)
		{
			pullRequest = await PreparePullRequestAsync();
			if (pullRequest is null)
				return 1;
		}

		var appliedConventions = new List<string>();
		var success = await ApplyConfigurationFileAsync(topLevelConfigPath, new HashSet<string>(StringComparer.Ordinal), appliedConventions);
		if (!success)
			return 1;

		if (pullRequest is not null)
			return await CompletePullRequestAsync(pullRequest, appliedConventions);

		return 0;
	}

	private async Task<bool> ApplyConfigurationFileAsync(string configPath, HashSet<string> activeConventions, List<string> appliedConventions)
	{
		var references = ConventionConfiguration.Load(configPath);
		var containingDirectory = Path.GetDirectoryName(configPath)!;

		foreach (var reference in references)
		{
			if (!await ApplyConventionReferenceAsync(reference, containingDirectory, activeConventions, appliedConventions))
				return false;
		}

		return true;
	}

	private async Task<bool> ApplyConventionReferenceAsync(ConventionReference reference, string containingDirectory, HashSet<string> activeConventions, List<string> appliedConventions)
	{
		var resolvedConvention = await ResolveConventionAsync(reference.Path, containingDirectory);
		if (activeConventions.Contains(resolvedConvention.Identity))
		{
			await m_standardOutput.WriteLineAsync($"Convention {resolvedConvention.DisplayName}: skipped (cycle detected).");
			return true;
		}

		activeConventions.Add(resolvedConvention.Identity);
		try
		{
			var conventionConfigPath = Path.Combine(resolvedConvention.DirectoryPath, "convention.yml");
			var conventionScriptPath = Path.Combine(resolvedConvention.DirectoryPath, "convention.ps1");
			if (!File.Exists(conventionConfigPath) && !File.Exists(conventionScriptPath))
			{
				await m_standardError.WriteLineAsync($"Convention '{reference.Path}' did not contain convention.yml or convention.ps1.");
				return false;
			}

			var createdCommit = false;
			if (File.Exists(conventionConfigPath))
			{
				if (!await ApplyConfigurationFileAsync(conventionConfigPath, activeConventions, appliedConventions))
					return false;
			}

			if (File.Exists(conventionScriptPath))
			{
				var scriptResult = await RunConventionScriptAsync(conventionScriptPath, reference.Settings, resolvedConvention.DisplayName);
				if (!scriptResult.Succeeded)
					return false;

				createdCommit = scriptResult.CreatedCommit;
			}

			appliedConventions.Add(resolvedConvention.DisplayName);
			await m_standardOutput.WriteLineAsync(createdCommit
				? $"Convention {resolvedConvention.DisplayName}: created commit."
				: $"Convention {resolvedConvention.DisplayName}: no changes.");
			return true;
		}
		finally
		{
			activeConventions.Remove(resolvedConvention.Identity);
		}
	}

	private async Task<ConventionExecutionResult> RunConventionScriptAsync(string scriptPath, JsonNode? settings, string conventionName)
	{
		var headBeforeConvention = await m_targetGitClient.GetHeadAsync();
		var payloadPath = Path.GetTempFileName();
		await File.WriteAllTextAsync(payloadPath, JsonSerializer.Serialize(new JsonObject { ["settings"] = settings }), m_cancellationToken);

		try
		{
			if (IsGitHubActions())
				await m_standardOutput.WriteLineAsync($"::group::Convention {conventionName}");

			var startInfo = new ProcessStartInfo("pwsh")
			{
				WorkingDirectory = m_targetRepositoryRoot,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
			};
			startInfo.ArgumentList.Add("-NoProfile");
			startInfo.ArgumentList.Add("-File");
			startInfo.ArgumentList.Add(scriptPath);
			startInfo.ArgumentList.Add(payloadPath);

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start pwsh.");
			var outputTask = PumpOutputAsync(process.StandardOutput, m_standardOutput);
			var errorTask = PumpOutputAsync(process.StandardError, m_standardError);
			await process.WaitForExitAsync(m_cancellationToken);
			await Task.WhenAll(outputTask, errorTask);

			if (IsGitHubActions())
				await m_standardOutput.WriteLineAsync("::endgroup::");

			if (process.ExitCode != 0)
			{
				await m_targetGitClient.ResetHardAsync(headBeforeConvention);
				await m_standardError.WriteLineAsync($"Convention {conventionName} failed.");
				return ConventionExecutionResult.Failed();
			}

			if (await m_targetGitClient.HasChangesAsync())
			{
				await m_targetGitClient.CommitAllAsync($"Apply convention {conventionName}.");
				return ConventionExecutionResult.Success(createdCommit: true);
			}

			return ConventionExecutionResult.Success(createdCommit: false);
		}
		finally
		{
			File.Delete(payloadPath);
		}
	}

	private async Task<ResolvedConvention> ResolveConventionAsync(string conventionPath, string containingDirectory)
	{
		if (conventionPath.StartsWith("./", StringComparison.Ordinal) || conventionPath.StartsWith("../", StringComparison.Ordinal))
		{
			var directoryPath = Path.GetFullPath(Path.Combine(containingDirectory, conventionPath));
			var name = new DirectoryInfo(directoryPath).Name;
			return new ResolvedConvention(directoryPath, name, directoryPath);
		}

		var remotePath = RemoteConventionPath.Parse(conventionPath);
		var cloneRoot = await GetOrCloneRemoteRepositoryAsync(remotePath);
		var resolvedDirectoryPath = string.IsNullOrEmpty(remotePath.SubPath)
			? cloneRoot
			: Path.Combine(cloneRoot, remotePath.SubPath.Replace('/', Path.DirectorySeparatorChar));
		var displayName = string.IsNullOrEmpty(remotePath.SubPath)
			? remotePath.Repository
			: new DirectoryInfo(resolvedDirectoryPath).Name;
		return new ResolvedConvention(resolvedDirectoryPath, displayName, remotePath.Identity);
	}

	private async Task<string> GetOrCloneRemoteRepositoryAsync(RemoteConventionPath remotePath)
	{
		if (m_remoteCloneCache.TryGetValue(remotePath.Identity, out var existingPath))
			return existingPath;

		var clonePath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Remote.{Guid.NewGuid():N}");
		Directory.CreateDirectory(clonePath);
		var repositoryUrl = GetRemoteRepositoryUrl(remotePath);

		var cloneResult = await GitClient.RunGitAsync(m_targetRepositoryRoot, m_cancellationToken, "clone", repositoryUrl, clonePath);
		if (cloneResult.ExitCode != 0)
			throw new InvalidOperationException($"Failed to clone remote convention repository '{repositoryUrl}': {cloneResult.StandardError}{cloneResult.StandardOutput}");

		if (!string.IsNullOrEmpty(remotePath.Ref))
		{
			var checkoutResult = await GitClient.RunGitAsync(clonePath, m_cancellationToken, "checkout", remotePath.Ref);
			if (checkoutResult.ExitCode != 0)
				throw new InvalidOperationException($"Failed to checkout ref '{remotePath.Ref}' in '{repositoryUrl}': {checkoutResult.StandardError}{checkoutResult.StandardOutput}");
		}

		m_remoteCloneCache.Add(remotePath.Identity, clonePath);
		return clonePath;
	}

	private string GetRemoteRepositoryUrl(RemoteConventionPath remotePath) =>
		m_remoteRepositoryUrlResolver?.Invoke(remotePath.Owner, remotePath.Repository) ?? $"https://github.com/{remotePath.Owner}/{remotePath.Repository}.git";

	private async Task<PullRequestPreparation?> PreparePullRequestAsync()
	{
		var startingBranch = await m_targetGitClient.GetCurrentBranchAsync();
		if (string.IsNullOrWhiteSpace(startingBranch))
		{
			await m_standardError.WriteLineAsync("The CLI must not be run from a detached HEAD when using --open-pr.");
			return null;
		}

		if (await m_targetGitClient.HasUnpushedCommitsAsync())
		{
			await m_standardError.WriteLineAsync("The CLI must be run with no unpushed commits when using --open-pr.");
			return null;
		}

		for (var suffix = 1; ; suffix++)
		{
			var branchName = suffix == 1 ? "repo-conventions" : $"repo-conventions-{suffix}";
			if (await HasOpenPullRequestAsync(branchName))
			{
				await m_targetGitClient.SwitchToExistingBranchAsync(branchName);
				return new PullRequestPreparation(startingBranch, branchName, HasOpenPullRequest: true);
			}

			if (!await m_targetGitClient.BranchExistsAsync(branchName))
			{
				await m_targetGitClient.SwitchToNewBranchAsync(branchName);
				return new PullRequestPreparation(startingBranch, branchName, HasOpenPullRequest: false);
			}
		}
	}

	private async Task<bool> HasOpenPullRequestAsync(string branchName)
	{
		var result = await RunExternalCommandAsync("gh", m_targetRepositoryRoot, ["pr", "list", "--state", "open", "--head", branchName, "--json", "number"]);
		if (result.ExitCode != 0)
			throw new InvalidOperationException($"Failed to query pull requests with gh: {result.StandardError}{result.StandardOutput}");

		return result.StandardOutput.Contains("number", StringComparison.Ordinal);
	}

	private async Task<int> CompletePullRequestAsync(PullRequestPreparation pullRequest, IReadOnlyList<string> appliedConventions)
	{
		var newCommits = await m_targetGitClient.RunAsync("rev-list", "--count", $"{pullRequest.StartingBranch}..HEAD");
		if (newCommits.ExitCode != 0)
			throw new InvalidOperationException($"Failed to compare branches: {newCommits.StandardError}{newCommits.StandardOutput}");

		if (newCommits.StandardOutput.Trim() == "0")
			return 0;

		await m_targetGitClient.PushBranchAsync(pullRequest.BranchName);
		if (pullRequest.HasOpenPullRequest)
			return 0;

		var body = BuildPullRequestBody(appliedConventions);
		var createResult = await RunExternalCommandAsync("gh", m_targetRepositoryRoot, ["pr", "create", "--base", pullRequest.StartingBranch, "--head", pullRequest.BranchName, "--title", "Apply repository conventions", "--body", body]);
		if (createResult.ExitCode != 0)
		{
			await m_standardError.WriteLineAsync($"Failed to create pull request: {createResult.StandardError}{createResult.StandardOutput}");
			return 1;
		}

		return 0;
	}

	private static string BuildPullRequestBody(IReadOnlyList<string> appliedConventions)
	{
		var lines = new List<string>
		{
			"This PR was generated by repo-conventions.",
			"",
			"Applied conventions:",
		};

		lines.AddRange(appliedConventions.Select(static convention => $"- {convention}"));
		return string.Join(Environment.NewLine, lines);
	}

	private static async Task PumpOutputAsync(StreamReader reader, TextWriter writer)
	{
		while (await reader.ReadLineAsync() is { } line)
			await writer.WriteLineAsync(line);
	}

	private static bool IsGitHubActions() => string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

	private static async Task<ExternalCommandResult> RunExternalCommandAsync(string fileName, string workingDirectory, string[] arguments)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
		var standardOutput = await process.StandardOutput.ReadToEndAsync();
		var standardError = await process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();

		return new ExternalCommandResult(process.ExitCode, standardOutput, standardError);
	}

	private sealed record ConventionExecutionResult(bool Succeeded, bool CreatedCommit)
	{
		public static ConventionExecutionResult Failed() => new(false, false);

		public static ConventionExecutionResult Success(bool createdCommit) => new(true, createdCommit);
	}

	private sealed record PullRequestPreparation(string StartingBranch, string BranchName, bool HasOpenPullRequest);

	private sealed record ResolvedConvention(string DirectoryPath, string DisplayName, string Identity);

	private sealed record ExternalCommandResult(int ExitCode, string StandardOutput, string StandardError);

	private sealed record RemoteConventionPath(string Owner, string Repository, string SubPath, string? Ref)
	{
		public string Identity => $"{Owner}/{Repository}:{SubPath}@{Ref}";

		public static RemoteConventionPath Parse(string conventionPath)
		{
			var refSeparatorIndex = conventionPath.LastIndexOf('@');
			var pathWithoutRef = refSeparatorIndex >= 0 ? conventionPath[..refSeparatorIndex] : conventionPath;
			var reference = refSeparatorIndex >= 0 ? conventionPath[(refSeparatorIndex + 1)..] : null;

			var segments = pathWithoutRef.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length < 2)
				throw new InvalidOperationException($"Convention path '{conventionPath}' is not a valid remote convention path.");

			var owner = segments[0];
			var repository = segments[1];
			var subPath = segments.Length > 2 ? string.Join('/', segments.Skip(2)) : "";
			return new RemoteConventionPath(owner, repository, subPath, reference);
		}
	}

	private readonly CancellationToken m_cancellationToken;
	private readonly Dictionary<string, string> m_remoteCloneCache = new(StringComparer.Ordinal);
	private readonly Func<string, string, string>? m_remoteRepositoryUrlResolver;
	private readonly TextWriter m_standardError;
	private readonly TextWriter m_standardOutput;
	private readonly string m_targetRepositoryRoot;
	private readonly GitClient m_targetGitClient;
}
