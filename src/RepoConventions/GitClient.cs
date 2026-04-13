using System.Diagnostics;

namespace RepoConventions;

internal sealed class GitClient
{
	public GitClient(string repositoryRoot) => RepositoryRoot = repositoryRoot;

	public string RepositoryRoot { get; }

	public Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) => RunGitAsync(RepositoryRoot, arguments, cancellationToken);

	public async Task<string> GetHeadAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(["rev-parse", "HEAD"], cancellationToken);
		EnsureSuccess(result, "rev-parse HEAD");
		return result.StandardOutput.Trim();
	}

	public async Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(["branch", "--show-current"], cancellationToken);
		EnsureSuccess(result, "branch --show-current");
		return result.StandardOutput.Trim();
	}

	public async Task<bool> HasUnpushedCommitsAsync(CancellationToken cancellationToken)
	{
		var upstream = await RunAsync(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], cancellationToken);
		if (upstream.ExitCode != 0)
			return true;

		var result = await RunAsync(["rev-list", "--count", "@{u}..HEAD"], cancellationToken);
		EnsureSuccess(result, "rev-list --count @{u}..HEAD");
		return result.StandardOutput.Trim() != "0";
	}

	public async Task<bool> BranchExistsAsync(string branchName, CancellationToken cancellationToken)
	{
		var local = await RunAsync(["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"], cancellationToken);
		if (local.ExitCode == 0)
			return true;

		var remote = await RunAsync(["ls-remote", "--exit-code", "--heads", "origin", branchName], cancellationToken);
		return remote.ExitCode == 0;
	}

	public async Task<bool> HasChangesAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(["status", "--porcelain", "--untracked-files=normal"], cancellationToken);
		EnsureSuccess(result, "status --porcelain --untracked-files=normal");
		return !string.IsNullOrWhiteSpace(result.StandardOutput);
	}

	public async Task CommitAllAsync(string message, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(["add", "-A"], cancellationToken), "add -A");
		EnsureSuccess(await RunAsync(["commit", "-m", message], cancellationToken), $"commit -m {message}");
	}

	public async Task ResetHardAsync(string commit, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(["reset", "--hard", commit], cancellationToken), $"reset --hard {commit}");
		EnsureSuccess(await RunAsync(["clean", "-fd"], cancellationToken), "clean -fd");
	}

	public async Task SwitchToNewBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(["switch", "-c", branchName], cancellationToken), $"switch -c {branchName}");
	}

	public async Task SwitchToExistingBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		var local = await RunAsync(["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"], cancellationToken);
		if (local.ExitCode == 0)
		{
			EnsureSuccess(await RunAsync(["switch", branchName], cancellationToken), $"switch {branchName}");
			return;
		}

		EnsureSuccess(await RunAsync(["switch", "-c", branchName, "--track", $"origin/{branchName}"], cancellationToken), $"switch -c {branchName} --track origin/{branchName}");
	}

	public async Task PushBranchAsync(string branchName, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(["push", "-u", "origin", branchName], cancellationToken), $"push -u origin {branchName}");
	}

	public static async Task<GitCommandResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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
