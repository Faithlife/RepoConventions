using System.Diagnostics;
using System.Globalization;
using System.Text;

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

	public async Task<GitMergeBaseResult> TryGetMergeBaseAsync(string firstRevision, string secondRevision, CancellationToken cancellationToken)
	{
		var result = await RunAsync(["merge-base", firstRevision, secondRevision], cancellationToken);
		if (result.ExitCode == 0)
			return GitMergeBaseResult.Success(result.StandardOutput.Trim(), result);

		var shallowResult = await RunAsync(["rev-parse", "--is-shallow-repository"], cancellationToken);
		var isShallowRepository = shallowResult.ExitCode == 0 && string.Equals(shallowResult.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
		return GitMergeBaseResult.Failure(result, isShallowRepository);
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

	public async Task<bool> HasStagedChangesAsync(CancellationToken cancellationToken)
	{
		var result = await RunAsync(["diff", "--cached", "--quiet", "--exit-code"], cancellationToken);
		return result.ExitCode switch
		{
			0 => false,
			1 => true,
			_ => throw new InvalidOperationException($"git diff --cached --quiet --exit-code failed: {result.StandardError}{result.StandardOutput}"),
		};
	}

	public async Task<bool> CommitAllAsync(string message, bool gitNoVerify, CancellationToken cancellationToken)
	{
		EnsureSuccess(await RunAsync(["add", "-A"], cancellationToken), "add -A");
		if (!await HasStagedChangesAsync(cancellationToken))
			return false;

		var arguments = new List<string> { "commit", "-m", message };
		if (gitNoVerify)
			arguments.Add("--no-verify");

		EnsureSuccess(await RunAsync(arguments, cancellationToken), $"{string.Join(' ', arguments)}");
		return true;
	}

	public async Task<int> CountCommitsSinceAsync(string commit, CancellationToken cancellationToken)
	{
		var result = await RunAsync(["rev-list", "--count", $"{commit}..HEAD"], cancellationToken);
		EnsureSuccess(result, $"rev-list --count {commit}..HEAD");
		return int.Parse(result.StandardOutput.Trim(), CultureInfo.InvariantCulture);
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

		EnsureSuccess(
			await RunAsync(["fetch", "origin", $"+refs/heads/{branchName}:refs/remotes/origin/{branchName}"], cancellationToken),
			$"fetch origin +refs/heads/{branchName}:refs/remotes/origin/{branchName}");

		EnsureSuccess(await RunAsync(["switch", "-c", branchName, "--track", $"origin/{branchName}"], cancellationToken), $"switch -c {branchName} --track origin/{branchName}");
	}

	public async Task PushBranchAsync(string branchName, bool force, bool gitNoVerify, CancellationToken cancellationToken)
	{
		var arguments = new List<string> { "push" };
		if (force)
			arguments.Add("--force-with-lease");
		if (gitNoVerify)
			arguments.Add("--no-verify");
		arguments.AddRange(["-u", "origin", branchName]);
		EnsureSuccess(await RunAsync(arguments, cancellationToken), string.Join(' ', arguments));
	}

	public static async Task<GitCommandResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo("git")
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			StandardErrorEncoding = Encoding.UTF8,
			StandardOutputEncoding = Encoding.UTF8,
			UseShellExecute = false,
		};

		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
		var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
		var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
		try
		{
			await ProcessRunner.WaitForExitAsync(process, cancellationToken);
		}
		finally
		{
			if (process.HasExited)
				await Task.WhenAll(standardOutputTask, standardErrorTask);
		}

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
