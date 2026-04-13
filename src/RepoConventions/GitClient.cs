using System.Diagnostics;

namespace RepoConventions;

internal sealed class GitClient
{
	public GitClient(string repositoryRoot, CancellationToken cancellationToken)
	{
		RepositoryRoot = repositoryRoot;
		CancellationToken = cancellationToken;
	}

	public string RepositoryRoot { get; }

	private CancellationToken CancellationToken { get; }

	public Task<GitCommandResult> RunAsync(params string[] arguments) => RunGitAsync(RepositoryRoot, CancellationToken, arguments);

	public async Task<string> GetHeadAsync()
	{
		var result = await RunAsync("rev-parse", "HEAD");
		EnsureSuccess(result, "rev-parse HEAD");
		return result.StandardOutput.Trim();
	}

	public async Task<string> GetCurrentBranchAsync()
	{
		var result = await RunAsync("branch", "--show-current");
		EnsureSuccess(result, "branch --show-current");
		return result.StandardOutput.Trim();
	}

	public async Task<bool> HasUnpushedCommitsAsync()
	{
		var upstream = await RunAsync("rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}");
		if (upstream.ExitCode != 0)
			return true;

		var result = await RunAsync("rev-list", "--count", "@{u}..HEAD");
		EnsureSuccess(result, "rev-list --count @{u}..HEAD");
		return result.StandardOutput.Trim() != "0";
	}

	public async Task<bool> BranchExistsAsync(string branchName)
	{
		var local = await RunAsync("show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
		if (local.ExitCode == 0)
			return true;

		var remote = await RunAsync("ls-remote", "--exit-code", "--heads", "origin", branchName);
		return remote.ExitCode == 0;
	}

	public async Task<bool> HasChangesAsync()
	{
		var result = await RunAsync("status", "--porcelain", "--untracked-files=normal");
		EnsureSuccess(result, "status --porcelain --untracked-files=normal");
		return !string.IsNullOrWhiteSpace(result.StandardOutput);
	}

	public async Task CommitAllAsync(string message)
	{
		EnsureSuccess(await RunAsync("add", "-A"), "add -A");
		EnsureSuccess(await RunAsync("commit", "-m", message), $"commit -m {message}");
	}

	public async Task ResetHardAsync(string commit)
	{
		EnsureSuccess(await RunAsync("reset", "--hard", commit), $"reset --hard {commit}");
		EnsureSuccess(await RunAsync("clean", "-fd"), "clean -fd");
	}

	public async Task SwitchToNewBranchAsync(string branchName)
	{
		EnsureSuccess(await RunAsync("switch", "-c", branchName), $"switch -c {branchName}");
	}

	public async Task SwitchToExistingBranchAsync(string branchName)
	{
		var local = await RunAsync("show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
		if (local.ExitCode == 0)
		{
			EnsureSuccess(await RunAsync("switch", branchName), $"switch {branchName}");
			return;
		}

		EnsureSuccess(await RunAsync("switch", "-c", branchName, "--track", $"origin/{branchName}"), $"switch -c {branchName} --track origin/{branchName}");
	}

	public async Task PushBranchAsync(string branchName)
	{
		EnsureSuccess(await RunAsync("push", "-u", "origin", branchName), $"push -u origin {branchName}");
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
