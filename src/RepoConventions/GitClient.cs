using System.Diagnostics;

namespace RepoConventions;

internal sealed class GitClient
{
	public GitClient(string repositoryRoot) => RepositoryRoot = repositoryRoot;

	public string RepositoryRoot { get; }

	public Task<GitCommandResult> RunAsync(CancellationToken cancellationToken, params string[] arguments) => RunGitAsync(RepositoryRoot, cancellationToken, arguments);

	public async Task<string> GetHeadAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(cancellationToken, "rev-parse", "HEAD");
		EnsureSuccess(result, "rev-parse HEAD");
		return result.StandardOutput.Trim();
	}

	public async Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(cancellationToken, "branch", "--show-current");
		EnsureSuccess(result, "branch --show-current");
		return result.StandardOutput.Trim();
	}

	public async Task<bool> HasUnpushedCommitsAsync(CancellationToken cancellationToken)
	{
		var upstream = await RunAsync(cancellationToken, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
		if (upstream.ExitCode != 0)
			return true;

		var result = await RunAsync(cancellationToken, "rev-list", "--count", "@{u}..HEAD");
		EnsureSuccess(result, "rev-list --count @{u}..HEAD");
		return result.StandardOutput.Trim() != "0";
	}

	public async Task<bool> BranchExistsAsync(string branchName, CancellationToken cancellationToken)
	{
		var local = await RunAsync(cancellationToken, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
		if (local.ExitCode == 0)
			return true;

		var remote = await RunAsync(cancellationToken, "ls-remote", "--exit-code", "--heads", "origin", branchName);
		return remote.ExitCode == 0;
	}

	public async Task<bool> HasChangesAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(cancellationToken, "status", "--porcelain", "--untracked-files=normal");
		EnsureSuccess(result, "status --porcelain --untracked-files=normal");
		return !string.IsNullOrWhiteSpace(result.StandardOutput);
	}

	public async Task CommitAllAsync(string message, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(cancellationToken, "add", "-A"), "add -A");
		EnsureSuccess(await RunAsync(cancellationToken, "commit", "-m", message), $"commit -m {message}");
	}

	public async Task ResetHardAsync(string commit, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(cancellationToken, "reset", "--hard", commit), $"reset --hard {commit}");
		EnsureSuccess(await RunAsync(cancellationToken, "clean", "-fd"), "clean -fd");
	}

	public async Task SwitchToNewBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(cancellationToken, "switch", "-c", branchName), $"switch -c {branchName}");
	}

	public async Task SwitchToExistingBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		var local = await RunAsync(cancellationToken, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
		if (local.ExitCode == 0)
		{
			EnsureSuccess(await RunAsync(cancellationToken, "switch", branchName), $"switch {branchName}");
			return;
		}

		EnsureSuccess(await RunAsync(cancellationToken, "switch", "-c", branchName, "--track", $"origin/{branchName}"), $"switch -c {branchName} --track origin/{branchName}");
	}

	public async Task PushBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(cancellationToken, "push", "-u", "origin", branchName), $"push -u origin {branchName}");
	}

	public static async Task<GitCommandResult> RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
	{
		var startInfo = new ProcessStartInfo("git")
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
		var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);

		return new GitCommandResult(
			process.ExitCode,
			await standardOutputTask,
			await standardErrorTask);
	}

	private static void EnsureSuccess(GitCommandResult result, string command)
	{
		if (result.ExitCode != 0)
			throw new InvalidOperationException($"git {command} failed: {result.StandardError}{result.StandardOutput}");
	}
}
