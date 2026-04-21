using System.Diagnostics;
using System.Globalization;
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

		var plannedConventions = new List<PlannedConvention>();
		var planSucceeded = await BuildConventionPlanAsync(topLevelConfigPath, new HashSet<string>(StringComparer.Ordinal), plannedConventions, parentSettings: null, expandCompositeSettings: false, sourceConventionIdentity: null, sourceConventionName: null, cancellationToken);
		if (!planSucceeded)
			return 1;

		await m_settings.StandardOutput.WriteLineAsync($"Applying {plannedConventions.Count.ToString(CultureInfo.InvariantCulture)} conventions...");

		var appliedConventions = new List<AppliedConvention>();
		var success = await ApplyConventionPlanAsync(plannedConventions, appliedConventions, cancellationToken);
		if (!success)
			return 1;

		if (pullRequest is not null)
			return await CompletePullRequestAsync(pullRequest, appliedConventions, cancellationToken);

		return 0;
	}

	private async Task<bool> BuildConventionPlanAsync(string configPath, HashSet<string> activeConventions, List<PlannedConvention> plannedConventions, JsonNode? parentSettings, bool expandCompositeSettings, string? sourceConventionIdentity, string? sourceConventionName, CancellationToken cancellationToken)
	{
		var references = ConventionConfiguration.Load(configPath);
		var containingDirectory = Path.GetDirectoryName(configPath)!;

		foreach (var reference in references)
		{
			if (!await AddConventionToPlanAsync(reference, containingDirectory, activeConventions, plannedConventions, parentSettings, expandCompositeSettings, sourceConventionIdentity, sourceConventionName, cancellationToken))
				return false;
		}

		return true;
	}

	private async Task<bool> AddConventionToPlanAsync(ConventionReference reference, string containingDirectory, HashSet<string> activeConventions, List<PlannedConvention> plannedConventions, JsonNode? parentSettings, bool expandCompositeSettings, string? sourceConventionIdentity, string? sourceConventionName, CancellationToken cancellationToken)
	{
		JsonNode? effectiveSettings;
		try
		{
			effectiveSettings = expandCompositeSettings
				? ConventionSettingsPropagator.Resolve(reference.Settings, parentSettings, sourceConventionName ?? "top-level", reference.Path)
				: reference.Settings?.DeepClone();
		}
		catch (InvalidOperationException ex)
		{
			await m_settings.StandardError.WriteLineAsync(ex.Message);
			return false;
		}

		var resolvedConvention = await ResolveConventionAsync(reference.Path, containingDirectory, cancellationToken);
		if (activeConventions.Contains(resolvedConvention.Identity))
		{
			await m_settings.StandardOutput.WriteLineAsync($"Convention {resolvedConvention.DisplayName}: skipped (cycle detected).");
			return true;
		}

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

		activeConventions.Add(resolvedConvention.Identity);
		try
		{
			if (File.Exists(conventionConfigPath))
			{
				if (!await BuildConventionPlanAsync(conventionConfigPath, activeConventions, plannedConventions, effectiveSettings, expandCompositeSettings: true, resolvedConvention.Identity, resolvedConvention.DisplayName, cancellationToken))
					return false;
			}

			plannedConventions.Add(new PlannedConvention(resolvedConvention, effectiveSettings, sourceConventionIdentity, sourceConventionName));
			return true;
		}
		finally
		{
			activeConventions.Remove(resolvedConvention.Identity);
		}
	}

	private async Task<bool> ApplyConventionPlanAsync(IReadOnlyList<PlannedConvention> plannedConventions, List<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		foreach (var plannedConvention in plannedConventions)
		{
			if (!await ApplyPlannedConventionAsync(plannedConvention, appliedConventions, cancellationToken))
				return false;
		}

		return true;
	}

	private async Task<bool> ApplyPlannedConventionAsync(PlannedConvention plannedConvention, List<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		var startMessage = $"Convention {FormatApplyingConventionName(plannedConvention)}";
		var openedGitHubActionsGroup = IsRunningInGitHubActions();
		if (openedGitHubActionsGroup)
		{
			await m_settings.StandardOutput.WriteLineAsync($"::group::{startMessage}");
		}
		else
		{
			await m_settings.StandardOutput.WriteLineAsync();
			await m_settings.StandardOutput.WriteLineAsync(startMessage);
		}

		try
		{
			var headBeforeConvention = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
			var conventionScriptPath = Path.Combine(plannedConvention.ResolvedConvention.DirectoryPath, "convention.ps1");
			if (File.Exists(conventionScriptPath))
			{
				var scriptResult = await RunConventionScriptAsync(conventionScriptPath, plannedConvention.Settings, plannedConvention.ResolvedConvention.DisplayName, cancellationToken);
				if (!scriptResult.Succeeded)
					return false;
			}

			var createdCommitCount = await m_settings.TargetGitClient.CountCommitsSinceAsync(headBeforeConvention, cancellationToken);
			appliedConventions.Add(new AppliedConvention(
				plannedConvention.ResolvedConvention.Identity,
				plannedConvention.ResolvedConvention.DisplayName,
				plannedConvention.ResolvedConvention.TargetRepositoryRelativePath,
				plannedConvention.ResolvedConvention.RemoteDirectory,
				plannedConvention.SourceConventionIdentity));
			await m_settings.StandardOutput.WriteLineAsync(createdCommitCount switch
			{
				0 => $"No changes for convention {plannedConvention.ResolvedConvention.DisplayName}.",
				1 => $"Created 1 commit for convention {plannedConvention.ResolvedConvention.DisplayName}.",
				_ => $"Created {createdCommitCount} commits for convention {plannedConvention.ResolvedConvention.DisplayName}.",
			});

			return true;
		}
		finally
		{
			if (openedGitHubActionsGroup)
				await m_settings.StandardOutput.WriteLineAsync("::endgroup::");
		}
	}

	private async Task<ConventionExecutionResult> RunConventionScriptAsync(string scriptPath, JsonNode? settings, string conventionName, CancellationToken cancellationToken)
	{
		var headBeforeConvention = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
		var inputPath = Path.GetTempFileName();
		await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(new JsonObject { ["settings"] = settings }), cancellationToken);

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
			startInfo.ArgumentList.Add(inputPath);

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
			File.Delete(inputPath);
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

		var clonePath = TemporaryDirectoryPath.Create();
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

		var startingHead = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);

		var openPullRequests = await GetOpenPullRequestsAsync(cancellationToken);
		var matchingPullRequests = openPullRequests
			.Where(x => x.BaseRefName == startingBranch)
			.Where(x => TryParseConventionBranchSuffix(x.HeadRefName) is not null)
			.ToList();
		if (matchingPullRequests.Count > 1)
		{
			var urls = string.Join(", ", matchingPullRequests.Select(static x => x.Url));
			await m_settings.StandardError.WriteLineAsync($"Multiple repo-conventions pull requests are already open for {startingBranch}: {urls}");
			return null;
		}

		if (matchingPullRequests.Count == 1)
		{
			var matchingPullRequest = matchingPullRequests[0];
			if (await m_settings.TargetGitClient.BranchExistsAsync(matchingPullRequest.HeadRefName, cancellationToken))
				await m_settings.TargetGitClient.SwitchToExistingBranchAsync(matchingPullRequest.HeadRefName, cancellationToken);
			else
				await m_settings.TargetGitClient.SwitchToNewBranchAsync(matchingPullRequest.HeadRefName, cancellationToken);

			var existingBranchHead = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
			var mergeBaseResult = await m_settings.TargetGitClient.TryGetMergeBaseAsync(startingBranch, matchingPullRequest.HeadRefName, cancellationToken);
			if (!mergeBaseResult.Succeeded)
			{
				await m_settings.StandardError.WriteLineAsync(BuildMergeBaseFailureMessage(startingBranch, matchingPullRequest.HeadRefName, mergeBaseResult));
				return null;
			}

			var shouldRestartFromBase = mergeBaseResult.MergeBase != startingHead;
			if (shouldRestartFromBase)
				await m_settings.TargetGitClient.ResetHardAsync(startingHead, cancellationToken);

			return new PullRequestPreparation(
				startingBranch,
				matchingPullRequest.HeadRefName,
				matchingPullRequest.Url,
				matchingPullRequest.Body,
				HasOpenPullRequest: true,
				ExistingBranchHead: existingBranchHead,
				ForcePushAfterUpdate: shouldRestartFromBase,
				RestartedFromBase: shouldRestartFromBase);
		}

		var openPullRequestBranches = openPullRequests
			.Select(x => x.HeadRefName)
			.Where(x => TryParseConventionBranchSuffix(x) is not null)
			.ToHashSet(StringComparer.Ordinal);

		for (var suffix = 1; ; suffix++)
		{
			var branchName = GetConventionBranchName(suffix);
			if (openPullRequestBranches.Contains(branchName))
				continue;

			var branchExists = await m_settings.TargetGitClient.BranchExistsAsync(branchName, cancellationToken);
			if (!branchExists)
			{
				await m_settings.TargetGitClient.SwitchToNewBranchAsync(branchName, cancellationToken);
				return new PullRequestPreparation(startingBranch, branchName, PullRequestUrl: null, ExistingPullRequestBody: "", HasOpenPullRequest: false, ExistingBranchHead: null, ForcePushAfterUpdate: false, RestartedFromBase: false);
			}
		}
	}

	private async Task<IReadOnlyList<GitHubPullRequest>> GetOpenPullRequestsAsync(CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "list", "--state", "open", "--json", "url,headRefName,baseRefName,body"], cancellationToken);
		if (result.ExitCode != 0)
			throw new InvalidOperationException($"Failed to query pull requests with gh: {result.StandardError}{result.StandardOutput}");

		try
		{
			return JsonSerializer.Deserialize<IReadOnlyList<GitHubPullRequest>>(string.IsNullOrWhiteSpace(result.StandardOutput) ? "[]" : result.StandardOutput, s_pullRequestJsonSerializerOptions) ?? [];
		}
		catch (JsonException ex)
		{
			throw new InvalidOperationException($"Failed to parse pull requests from gh: {result.StandardOutput}{result.StandardError}", ex);
		}
	}

	private async Task<int> CompletePullRequestAsync(PullRequestPreparation pullRequest, IReadOnlyList<AppliedConvention> appliedConventions, CancellationToken cancellationToken)
	{
		var newCommits = await m_settings.TargetGitClient.RunAsync(["rev-list", "--count", $"{pullRequest.StartingBranch}..HEAD"], cancellationToken);
		if (newCommits.ExitCode != 0)
			throw new InvalidOperationException($"Failed to compare branches: {newCommits.StandardError}{newCommits.StandardOutput}");

		var pullRequestCommitCount = int.Parse(newCommits.StandardOutput.Trim(), CultureInfo.InvariantCulture);
		var targetRepositoryUrl = await GetTargetRepositoryUrlAsync(cancellationToken);
		var body = BuildPullRequestBody(appliedConventions, targetRepositoryUrl, pullRequest.BranchName);
		if (pullRequest.HasOpenPullRequest)
			return await CompleteExistingPullRequestAsync(pullRequest, pullRequestCommitCount, body, cancellationToken);

		if (pullRequestCommitCount == 0)
			return 0;

		await m_settings.TargetGitClient.PushBranchAsync(pullRequest.BranchName, force: false, cancellationToken);
		if (pullRequest.HasOpenPullRequest)
			return 0;

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

	private async Task<int> CompleteExistingPullRequestAsync(PullRequestPreparation pullRequest, int pullRequestCommitCount, string pullRequestBody, CancellationToken cancellationToken)
	{
		if (pullRequest.PullRequestUrl is null)
			throw new InvalidOperationException("Existing pull request URL was not provided.");

		var currentHead = await m_settings.TargetGitClient.GetHeadAsync(cancellationToken);
		var commitsChanged = pullRequest.ExistingBranchHead != currentHead;
		var bodyChanged = pullRequest.ExistingPullRequestBody != pullRequestBody;

		if (pullRequestCommitCount == 0)
		{
			var comment = "No convention commits remain.";
			if (!await AddPullRequestCommentAsync(pullRequest.PullRequestUrl, comment, cancellationToken))
				return 1;

			if (!await ClosePullRequestAsync(pullRequest.PullRequestUrl, cancellationToken))
				return 1;

			await m_settings.StandardOutput.WriteLineAsync($"Closed pull request: {pullRequest.PullRequestUrl}");
			return 0;
		}

		if (commitsChanged)
			await m_settings.TargetGitClient.PushBranchAsync(pullRequest.BranchName, pullRequest.ForcePushAfterUpdate, cancellationToken);

		if (bodyChanged)
		{
			if (!await UpdatePullRequestBodyAsync(pullRequest.PullRequestUrl, pullRequestBody, cancellationToken))
				return 1;
		}

		if (commitsChanged || bodyChanged)
		{
			await m_settings.StandardOutput.WriteLineAsync($"Updated pull request: {pullRequest.PullRequestUrl}");
			return 0;
		}

		await m_settings.StandardOutput.WriteLineAsync($"Pull request: {pullRequest.PullRequestUrl}");
		return 0;
	}

	private static string BuildPullRequestBody(IReadOnlyList<AppliedConvention> appliedConventions, string? targetRepositoryUrl, string branchName)
	{
		var lines = new List<string>
		{
			$"{FormatConventionsLabel(targetRepositoryUrl, branchName)} applied by [repo-conventions](https://github.com/Faithlife/RepoConventions):",
		};

		lines.AddRange(RenderAppliedConventionLines(appliedConventions, targetRepositoryUrl, branchName));
		return string.Join(Environment.NewLine, lines);
	}

	private static IEnumerable<string> RenderAppliedConventionLines(IReadOnlyList<AppliedConvention> appliedConventions, string? targetRepositoryUrl, string branchName)
	{
		var nodes = appliedConventions.ToDictionary(static x => x.Identity, static x => new AppliedConventionNode(x), StringComparer.Ordinal);
		var roots = new List<AppliedConventionNode>();

		foreach (var convention in appliedConventions)
		{
			var node = nodes[convention.Identity];
			if (convention.SourceConventionIdentity is { } sourceConventionIdentity && nodes.TryGetValue(sourceConventionIdentity, out var parent))
				parent.Children.Add(node);
			else
				roots.Add(node);
		}

		foreach (var root in roots)
		{
			foreach (var line in RenderAppliedConventionNode(root, targetRepositoryUrl, branchName, depth: 0))
				yield return line;
		}
	}

	private static IEnumerable<string> RenderAppliedConventionNode(AppliedConventionNode node, string? targetRepositoryUrl, string branchName, int depth)
	{
		yield return $"{new string(' ', depth * 2)}- {FormatAppliedConvention(node.Convention, targetRepositoryUrl, branchName)}";

		foreach (var child in node.Children)
		{
			foreach (var line in RenderAppliedConventionNode(child, targetRepositoryUrl, branchName, depth + 1))
				yield return line;
		}
	}

	private static string FormatApplyingConventionName(PlannedConvention plannedConvention) =>
		plannedConvention.SourceConventionName is null
			? plannedConvention.ResolvedConvention.DisplayName
			: $"{plannedConvention.ResolvedConvention.DisplayName} (from {plannedConvention.SourceConventionName})";

	private static bool IsRunningInGitHubActions() =>
		string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

	private static string BuildMergeBaseFailureMessage(string startingBranch, string pullRequestBranch, GitMergeBaseResult mergeBaseResult)
	{
		var lines = new List<string>
		{
			$"repo-conventions could not compare '{startingBranch}' with existing pull request branch '{pullRequestBranch}'.",
			$"git merge-base {startingBranch} {pullRequestBranch} failed.",
		};

		var gitOutput = string.Concat(mergeBaseResult.CommandResult.StandardError, mergeBaseResult.CommandResult.StandardOutput).Trim();
		if (!string.IsNullOrEmpty(gitOutput))
			lines.Add($"git output: {gitOutput}");

		if (mergeBaseResult.IsShallowRepository)
		{
			lines.Add("This repository appears to be a shallow clone.");
			lines.Add("Suggested fixes: use actions/checkout with fetch-depth: 0, or run 'git fetch --prune --unshallow origin' before 'repo-conventions apply --open-pr'.");
		}
		else
		{
			lines.Add("Suggested fixes: fetch enough history for both branches, fetch the existing repo-conventions branch locally, or delete any stale local repo-conventions branch before rerunning.");
		}

		return string.Join(Environment.NewLine, lines);
	}

	private async Task<bool> AddPullRequestCommentAsync(string pullRequestUrl, string body, CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "comment", pullRequestUrl, "--body", body], cancellationToken);
		if (result.ExitCode == 0)
			return true;

		await m_settings.StandardError.WriteLineAsync($"Failed to add pull request comment: {result.StandardError}{result.StandardOutput}");
		return false;
	}

	private async Task<bool> UpdatePullRequestBodyAsync(string pullRequestUrl, string body, CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "edit", pullRequestUrl, "--body", body], cancellationToken);
		if (result.ExitCode == 0)
			return true;

		await m_settings.StandardError.WriteLineAsync($"Failed to update pull request body: {result.StandardError}{result.StandardOutput}");
		return false;
	}

	private async Task<bool> ClosePullRequestAsync(string pullRequestUrl, CancellationToken cancellationToken)
	{
		var result = await RunExternalCommandAsync("gh", m_settings.TargetRepositoryRoot, ["pr", "close", pullRequestUrl], cancellationToken);
		if (result.ExitCode == 0)
			return true;

		await m_settings.StandardError.WriteLineAsync($"Failed to close pull request: {result.StandardError}{result.StandardOutput}");
		return false;
	}

	private static async Task PumpOutputAsync(StreamReader reader, TextWriter writer)
	{
		while (await reader.ReadLineAsync() is { } line)
			await writer.WriteLineAsync(line);
	}

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

	private static string GetConventionBranchName(int suffix) => suffix == 1 ? "repo-conventions" : $"repo-conventions-{suffix}";

	private static int? TryParseConventionBranchSuffix(string branchName)
	{
		if (branchName == "repo-conventions")
			return 1;

		const string prefix = "repo-conventions-";
		if (!branchName.StartsWith(prefix, StringComparison.Ordinal))
			return null;

		return int.TryParse(branchName[prefix.Length..], out var suffix) && suffix >= 2 ? suffix : null;
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

	private sealed record GitHubPullRequest(string Url, string HeadRefName, string BaseRefName, string Body);

	private sealed record PullRequestPreparation(string StartingBranch, string BranchName, string? PullRequestUrl, string ExistingPullRequestBody, bool HasOpenPullRequest, string? ExistingBranchHead, bool ForcePushAfterUpdate, bool RestartedFromBase);

	private sealed record PlannedConvention(ResolvedConvention ResolvedConvention, JsonNode? Settings, string? SourceConventionIdentity, string? SourceConventionName);

	private sealed record AppliedConvention(string Identity, string DisplayName, string? TargetRepositoryRelativePath, RemoteDirectoryReference? RemoteDirectory, string? SourceConventionIdentity);

	private sealed class AppliedConventionNode(AppliedConvention convention)
	{
		public AppliedConvention Convention { get; } = convention;

		public List<AppliedConventionNode> Children { get; } = [];
	}

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

	private static readonly JsonSerializerOptions s_pullRequestJsonSerializerOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly ConventionRunnerSettings m_settings;
	private readonly Dictionary<string, string> m_remoteCloneCache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, RemoteRepositoryInfo> m_remoteRepositoryContexts = new(StringComparer.Ordinal);
}
