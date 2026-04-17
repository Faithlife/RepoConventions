using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoConventions;

internal sealed class ConventionRunner
{
	public ConventionRunner(ConventionRunnerSettings settings) => m_settings = settings;

	public async Task<int> RunAsync(string topLevelConfigPath, bool openPr, CancellationToken cancellationToken)
	{
		PullRequestPreparation? pullRequest = null;
		if (openPr)
		{
			pullRequest = await PreparePullRequestAsync(cancellationToken);
			if (pullRequest is null)
				return 1;
		}

		var appliedConventions = new List<AppliedConvention>();
		var success = await ApplyConfigurationFileAsync(topLevelConfigPath, new HashSet<string>(StringComparer.Ordinal), appliedConventions, cancellationToken);
		if (!success)
			return 1;

		if (pullRequest is not null)
			return await CompletePullRequestAsync(pullRequest, appliedConventions, cancellationToken);

		return 0;
	}

	private async Task<bool> ApplyConfigurationFileAsync(string configPath, HashSet<string> activeConventions, List<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		var references = ConventionConfiguration.Load(configPath);
		var containingDirectory = Path.GetDirectoryName(configPath)!;

		foreach (var reference in references)
		{
			if (!await ApplyConventionReferenceAsync(reference, containingDirectory, activeConventions, appliedConventions, cancellationToken))
				return false;
		}

		return true;
	}

	private async Task<bool> ApplyConventionReferenceAsync(ConventionReference reference, string containingDirectory, HashSet<string> activeConventions, List<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		var resolvedConvention = await ResolveConventionAsync(reference.Path, containingDirectory, cancellationToken);
		if (activeConventions.Contains(resolvedConvention.Identity))
		{
			await m_settings.StandardOutput.WriteLineAsync($"Convention {resolvedConvention.DisplayName}: skipped (cycle detected).");
			return true;
		}

		var isGitHubActions = IsGitHubActions();
		var headBeforeConvention = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
		activeConventions.Add(resolvedConvention.Identity);
		try
		{
			if (isGitHubActions)
				await m_settings.StandardOutput.WriteLineAsync($"::group::Convention {resolvedConvention.DisplayName}");

			await m_settings.StandardOutput.WriteLineAsync($"Convention {resolvedConvention.DisplayName}: applying...");

			if (!Directory.Exists(resolvedConvention.DirectoryPath))
			{
				await m_settings.StandardError.WriteLineAsync($"Convention '{reference.Path}' directory '{resolvedConvention.DirectoryPath}' was not found.");
				return false;
			}

			var conventionConfigPath = Path.Combine(resolvedConvention.DirectoryPath, "convention.yml");
			var conventionScriptPath = Path.Combine(resolvedConvention.DirectoryPath, "convention.ps1");
			if (!File.Exists(conventionConfigPath) && !File.Exists(conventionScriptPath))
			{
				await m_settings.StandardError.WriteLineAsync($"Convention '{reference.Path}' did not contain convention.yml or convention.ps1.");
				return false;
			}

			if (File.Exists(conventionConfigPath))
			{
				if (!await ApplyConfigurationFileAsync(conventionConfigPath, activeConventions, appliedConventions, cancellationToken))
					return false;
			}

			if (File.Exists(conventionScriptPath))
			{
				var scriptResult = await RunConventionScriptAsync(conventionScriptPath, reference.Settings, resolvedConvention.DisplayName, cancellationToken);
				if (!scriptResult.Succeeded)
					return false;
			}

			var createdCommitCount = await m_settings.TargetGitClient.CountCommitsSinceAsync(headBeforeConvention, cancellationToken);
			appliedConventions.Add(new AppliedConvention(resolvedConvention.DisplayName, resolvedConvention.TargetRepositoryRelativePath, resolvedConvention.RemoteDirectory));
			await m_settings.StandardOutput.WriteLineAsync(createdCommitCount switch
			{
				0 => $"Convention {resolvedConvention.DisplayName}: no changes.",
				1 => $"Convention {resolvedConvention.DisplayName}: created 1 commit.",
				_ => $"Convention {resolvedConvention.DisplayName}: created {createdCommitCount} commits.",
			});
			return true;
		}
		finally
		{
			if (isGitHubActions)
				await m_settings.StandardOutput.WriteLineAsync("::endgroup::");

			activeConventions.Remove(resolvedConvention.Identity);
		}
	}

	private async Task<ConventionExecutionResult> RunConventionScriptAsync(string scriptPath, JsonNode? settings, string conventionName, CancellationToken cancellationToken)
	{
		var headBeforeConvention = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
		var payloadPath = Path.GetTempFileName();
		await File.WriteAllTextAsync(payloadPath, JsonSerializer.Serialize(new JsonObject { ["settings"] = settings }), cancellationToken);

		try
		{
			var startInfo = new ProcessStartInfo("pwsh")
			{
				WorkingDirectory = m_settings.TargetRepositoryRoot,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
			};
			startInfo.ArgumentList.Add("-NoProfile");
			startInfo.ArgumentList.Add("-File");
			startInfo.ArgumentList.Add(scriptPath);
			startInfo.ArgumentList.Add(payloadPath);

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start pwsh.");
			var outputTask = PumpOutputAsync(process.StandardOutput, m_settings.StandardOutput);
			var errorTask = PumpOutputAsync(process.StandardError, m_settings.StandardError);
			await process.WaitForExitAsync(cancellationToken);
			await Task.WhenAll(outputTask, errorTask);

			if (process.ExitCode != 0)
			{
				await m_settings.TargetGitClient.ResetHardAsync(headBeforeConvention, cancellationToken);
				await m_settings.StandardError.WriteLineAsync($"Convention {conventionName} failed.");
				return ConventionExecutionResult.Failed();
			}

			if (await m_settings.TargetGitClient.HasChangesAsync(cancellationToken))
			{
				await m_settings.TargetGitClient.CommitAllAsync($"Apply convention {conventionName}.", cancellationToken);
			}

			return ConventionExecutionResult.Success();
		}
		finally
		{
			File.Delete(payloadPath);
		}
	}

	private async Task<ResolvedConvention> ResolveConventionAsync(string conventionPath, string containingDirectory, CancellationToken cancellationToken)
	{
		if (conventionPath.StartsWith('/'))
		{
			var directoryPath = Path.Combine(m_settings.TargetRepositoryRoot, conventionPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
			var name = new DirectoryInfo(directoryPath).Name;
			return new ResolvedConvention(directoryPath, name, directoryPath, GetTargetRepositoryRelativePath(directoryPath), null);
		}

		if (conventionPath.StartsWith("./", StringComparison.Ordinal) || conventionPath.StartsWith("../", StringComparison.Ordinal))
		{
			var directoryPath = Path.GetFullPath(Path.Combine(containingDirectory, conventionPath));
			var name = new DirectoryInfo(directoryPath).Name;

			if (TryGetRemoteCloneContext(directoryPath, out var remoteRepository, out var remoteCloneRoot))
			{
				var remoteDirectoryPath = NormalizeGitHubPath(Path.GetRelativePath(remoteCloneRoot, directoryPath));
				return new ResolvedConvention(directoryPath, name, directoryPath, null, new RemoteDirectoryReference(remoteRepository.Owner, remoteRepository.Repository, remoteRepository.Ref, remoteDirectoryPath));
			}

			return new ResolvedConvention(directoryPath, name, directoryPath, GetTargetRepositoryRelativePath(directoryPath), null);
		}

		var remotePath = RemoteConventionPath.Parse(conventionPath);
		var cloneRoot = await GetOrCloneRemoteRepositoryAsync(remotePath, cancellationToken);
		var resolvedDirectoryPath = string.IsNullOrEmpty(remotePath.SubPath)
			? cloneRoot
			: Path.Combine(cloneRoot, remotePath.SubPath.Replace('/', Path.DirectorySeparatorChar));
		var displayName = string.IsNullOrEmpty(remotePath.SubPath)
			? remotePath.Repository
			: new DirectoryInfo(resolvedDirectoryPath).Name;
		return new ResolvedConvention(resolvedDirectoryPath, displayName, remotePath.Identity, null, new RemoteDirectoryReference(remotePath.Owner, remotePath.Repository, remotePath.Ref, remotePath.SubPath));
	}

	private async Task<string> GetOrCloneRemoteRepositoryAsync(RemoteConventionPath remotePath, CancellationToken cancellationToken)
	{
		if (m_remoteCloneCache.TryGetValue(remotePath.Identity, out var existingPath))
			return existingPath;

		var clonePath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Remote.{Guid.NewGuid():N}");
		Directory.CreateDirectory(clonePath);
		var repositoryUrl = GetRemoteRepositoryUrl(remotePath);

		var cloneResult = await GitClient.RunGitAsync(m_settings.TargetRepositoryRoot, ["clone", repositoryUrl, clonePath], cancellationToken);
		if (cloneResult.ExitCode != 0)
			throw new InvalidOperationException($"Failed to clone remote convention repository '{repositoryUrl}': {cloneResult.StandardError}{cloneResult.StandardOutput}");

		if (!string.IsNullOrEmpty(remotePath.Ref))
		{
			var checkoutResult = await GitClient.RunGitAsync(clonePath, ["checkout", remotePath.Ref], cancellationToken);
			if (checkoutResult.ExitCode != 0)
				throw new InvalidOperationException($"Failed to checkout ref '{remotePath.Ref}' in '{repositoryUrl}': {checkoutResult.StandardError}{checkoutResult.StandardOutput}");
		}

		m_remoteCloneCache.Add(remotePath.Identity, clonePath);
		m_remoteRepositoryContexts[clonePath] = new RemoteRepositoryInfo(remotePath.Owner, remotePath.Repository, remotePath.Ref);
		return clonePath;
	}

	private string GetRemoteRepositoryUrl(RemoteConventionPath remotePath) =>
		m_settings.RemoteRepositoryUrlResolver?.Invoke(new RemoteRepositoryUrlRequest(remotePath.Owner, remotePath.Repository)) ?? $"https://github.com/{remotePath.Owner}/{remotePath.Repository}.git";

	private async Task<PullRequestPreparation?> PreparePullRequestAsync(CancellationToken cancellationToken)
	{
		var startingBranch = await m_settings.TargetGitClient.GetCurrentBranchAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(startingBranch))
		{
			await m_settings.StandardError.WriteLineAsync("repo-conventions must not be run from a detached HEAD when using --open-pr.");
			return null;
		}

		if (await m_settings.TargetGitClient.HasUnpushedCommitsAsync(cancellationToken))
		{
			await m_settings.StandardError.WriteLineAsync("repo-conventions must be run with no unpushed commits when using --open-pr.");
			return null;
		}

		for (var suffix = 1; ; suffix++)
		{
			var branchName = suffix == 1 ? "repo-conventions" : $"repo-conventions-{suffix}";
			if (await HasOpenPullRequestAsync(branchName, cancellationToken))
			{
				await m_settings.TargetGitClient.SwitchToExistingBranchAsync(branchName, cancellationToken);
				return new PullRequestPreparation(startingBranch, branchName, HasOpenPullRequest: true);
			}

			if (!await m_settings.TargetGitClient.BranchExistsAsync(branchName, cancellationToken))
			{
				await m_settings.TargetGitClient.SwitchToNewBranchAsync(branchName, cancellationToken);
				return new PullRequestPreparation(startingBranch, branchName, HasOpenPullRequest: false);
			}
		}
	}

	private async Task<bool> HasOpenPullRequestAsync(string branchName, CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "list", "--state", "open", "--head", branchName, "--json", "number"], cancellationToken);
		if (result.ExitCode != 0)
			throw new InvalidOperationException($"Failed to query pull requests with gh: {result.StandardError}{result.StandardOutput}");

		return result.StandardOutput.Contains("number", StringComparison.Ordinal);
	}

	private async Task<int> CompletePullRequestAsync(PullRequestPreparation pullRequest, IReadOnlyList<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		var newCommits = await m_settings.TargetGitClient.RunAsync(["rev-list", "--count", $"{pullRequest.StartingBranch}..HEAD"], cancellationToken);
		if (newCommits.ExitCode != 0)
			throw new InvalidOperationException($"Failed to compare branches: {newCommits.StandardError}{newCommits.StandardOutput}");

		if (newCommits.StandardOutput.Trim() == "0")
			return 0;

		await m_settings.TargetGitClient.PushBranchAsync(pullRequest.BranchName, cancellationToken);
		if (pullRequest.HasOpenPullRequest)
			return 0;

		var targetRepositoryUrl = await GetTargetRepositoryUrlAsync(cancellationToken);
		var body = BuildPullRequestBody(appliedConventions, targetRepositoryUrl, pullRequest.BranchName);
		await m_settings.StandardOutput.WriteLineAsync($"Opening pull request from {pullRequest.BranchName} to {pullRequest.StartingBranch}...");
		var createResult = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "create", "--base", pullRequest.StartingBranch, "--head", pullRequest.BranchName, "--title", "Apply repository conventions.", "--body", body], cancellationToken);
		if (createResult.ExitCode != 0)
		{
			await m_settings.StandardError.WriteLineAsync($"Failed to create pull request: {createResult.StandardError}{createResult.StandardOutput}");
			return 1;
		}

		var pullRequestUrl = ExtractGitHubPullRequestUrl(createResult.StandardOutput, createResult.StandardError);
		if (pullRequestUrl is not null)
			await m_settings.StandardOutput.WriteLineAsync($"Opened pull request: {pullRequestUrl}");
		else
			await m_settings.StandardOutput.WriteLineAsync("Opened pull request.");

		return 0;
	}

	private static string BuildPullRequestBody(IReadOnlyList<AppliedConvention> appliedConventions, string? targetRepositoryUrl, string branchName)
	{
		var lines = new List<string>
		{
			$"{FormatConventionsLabel(targetRepositoryUrl, branchName)} applied by [repo-conventions](https://github.com/Faithlife/RepoConventions):",
		};

		lines.AddRange(appliedConventions.Select(convention => $"- {FormatAppliedConvention(convention, targetRepositoryUrl, branchName)}"));
		return string.Join(Environment.NewLine, lines);
	}

	private static async Task PumpOutputAsync(StreamReader reader, TextWriter writer)
	{
		while (await reader.ReadLineAsync() is { } line)
			await writer.WriteLineAsync(line);
	}

	private static bool IsGitHubActions() => string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

	private static string FormatConventionsLabel(string? targetRepositoryUrl, string branchName)
	{
		if (targetRepositoryUrl is null)
			return "Conventions";

		return $"[Conventions]({targetRepositoryUrl}/blob/{branchName}/.github/conventions.yml)";
	}

	private static string FormatAppliedConvention(AppliedConvention convention, string? targetRepositoryUrl, string branchName)
	{
		var url = BuildAppliedConventionUrl(convention, targetRepositoryUrl, branchName);
		return url is null ? convention.DisplayName : $"[{convention.DisplayName}]({url})";
	}

	private static string? BuildAppliedConventionUrl(AppliedConvention convention, string? targetRepositoryUrl, string branchName)
	{
		if (convention.TargetRepositoryRelativePath is { } targetRepositoryRelativePath && targetRepositoryUrl is not null)
			return $"{targetRepositoryUrl}/tree/{branchName}/{targetRepositoryRelativePath}";

		if (convention.RemoteDirectory is { } remoteDirectory)
		{
			var repositoryUrl = $"https://github.com/{remoteDirectory.Owner}/{remoteDirectory.Repository}";
			if (string.IsNullOrEmpty(remoteDirectory.DirectoryPath))
				return remoteDirectory.Ref is null ? repositoryUrl : $"{repositoryUrl}/tree/{remoteDirectory.Ref}";

			return $"{repositoryUrl}/tree/{remoteDirectory.Ref ?? "HEAD"}/{remoteDirectory.DirectoryPath}";
		}

		return null;
	}

	private static string NormalizeGitHubPath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

	private string GetTargetRepositoryRelativePath(string directoryPath) => NormalizeGitHubPath(Path.GetRelativePath(m_settings.TargetRepositoryRoot, directoryPath));

	private bool TryGetRemoteCloneContext(string directoryPath, out RemoteRepositoryInfo remoteRepository, out string cloneRoot)
	{
		foreach (var candidate in m_remoteRepositoryContexts.OrderByDescending(static x => x.Key.Length))
		{
			var relativePath = Path.GetRelativePath(candidate.Key, directoryPath);
			if (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath))
			{
				remoteRepository = candidate.Value;
				cloneRoot = candidate.Key;
				return true;
			}
		}

		remoteRepository = null!;
		cloneRoot = null!;
		return false;
	}

	private async Task<string?> GetTargetRepositoryUrlAsync(CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["repo", "view", "--json", "url", "--jq", ".url"], cancellationToken);
		if (result.ExitCode != 0)
			return null;

		return ExtractGitHubPullRequestUrl(result.StandardOutput, result.StandardError) ?? result.StandardOutput.Trim();
	}

	private static string? ExtractGitHubPullRequestUrl(string standardOutput, string standardError)
	{
		foreach (var token in string.Concat(standardOutput, " ", standardError).Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
				string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
			{
				return uri.AbsoluteUri;
			}
		}

		return null;
	}

	private async Task<ExternalCommandResult> RunExternalCommandAsync(string fileName, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		if (m_settings.ExternalCommandRunner is { } externalCommandRunner)
			return await externalCommandRunner(new ExternalCommandRequest(fileName, workingDirectory, arguments), cancellationToken);

		return await RunExternalCommandCoreAsync(fileName, workingDirectory, arguments, cancellationToken);
	}

	private static async Task<ExternalCommandResult> RunExternalCommandCoreAsync(string fileName, string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
		var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
		var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return new ExternalCommandResult(process.ExitCode, standardOutput, standardError);
	}

	private sealed record ConventionExecutionResult(bool Succeeded)
	{
		public static ConventionExecutionResult Failed() => new(false);

		public static ConventionExecutionResult Success() => new(true);
	}

	private sealed record PullRequestPreparation(string StartingBranch, string BranchName, bool HasOpenPullRequest);

	private sealed record AppliedConvention(string DisplayName, string? TargetRepositoryRelativePath, RemoteDirectoryReference? RemoteDirectory);

	private sealed record RemoteDirectoryReference(string Owner, string Repository, string? Ref, string DirectoryPath);

	private sealed record RemoteRepositoryInfo(string Owner, string Repository, string? Ref);

	private sealed record ResolvedConvention(string DirectoryPath, string DisplayName, string Identity, string? TargetRepositoryRelativePath, RemoteDirectoryReference? RemoteDirectory);

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

	private readonly ConventionRunnerSettings m_settings;
	private readonly Dictionary<string, string> m_remoteCloneCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, RemoteRepositoryInfo> m_remoteRepositoryContexts = new(StringComparer.Ordinal);
}
