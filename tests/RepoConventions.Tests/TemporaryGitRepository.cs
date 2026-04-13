using System.Diagnostics;
using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class TemporaryGitRepository : IDisposable
{
	private TemporaryGitRepository(string rootPath, bool isBare)
	{
		RootPath = rootPath;
		IsBare = isBare;
	}

	public bool IsBare { get; }

	public string RootPath { get; }

	public static async Task<TemporaryGitRepository> CreateAsync()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Tests.{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootPath);

		await RunGitAsync(rootPath, ["init", "--initial-branch=main"]);
		await RunGitAsync(rootPath, ["config", "user.name", "RepoConventions Tests"]);
		await RunGitAsync(rootPath, ["config", "user.email", "repo-conventions-tests@example.com"]);

		return new TemporaryGitRepository(rootPath, isBare: false);
	}

	public static async Task<TemporaryGitRepository> CreateBareAsync()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Tests.Bare.{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootPath);
		await RunGitAsync(Path.GetTempPath(), ["init", "--bare", rootPath]);
		return new TemporaryGitRepository(rootPath, isBare: true);
	}

	public string CreateDirectory(string relativePath)
	{
		VerifyNotBare();
		var fullPath = Path.Combine(RootPath, relativePath);
		Directory.CreateDirectory(fullPath);
		return fullPath;
	}

	public void WriteFile(string relativePath, string contents)
	{
		VerifyNotBare();
		var fullPath = Path.Combine(RootPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		File.WriteAllText(fullPath, contents);
	}

	public bool FileExists(string relativePath) => File.Exists(Path.Combine(RootPath, relativePath));

	public string GetRepositoryUri() => new Uri(RootPath + Path.DirectorySeparatorChar).AbsoluteUri;

	public Task<string> ReadFileAsync(string relativePath)
	{
		VerifyNotBare();
		return File.ReadAllTextAsync(Path.Combine(RootPath, relativePath));
	}

	public async Task AddRemoteAsync(string name, string pathOrUri)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["remote", "add", name, pathOrUri]);
	}

	public async Task CommitAllAsync(string message)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["add", "-A"]);
		await RunGitAsync(RootPath, ["commit", "-m", message]);
	}

	public async Task CreateBranchAsync(string branchName)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["branch", branchName]);
	}

	public async Task CreateTagAsync(string tagName)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["tag", tagName]);
	}

	public async Task DeleteBranchAsync(string branchName)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["branch", "-D", branchName]);
	}

	public async Task DetachHeadAsync()
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["switch", "--detach", "HEAD"]);
	}

	public async Task<string> GetHeadCommitMessageAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, ["log", "-1", "--pretty=%B"])).Trim();

	public async Task<string> GetHeadCommitShaAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, ["rev-parse", "HEAD"])).Trim();

	public async Task<string> GetHeadCommitShaAsync(string branchName) =>
		(await RunGitAndCaptureOutputAsync(RootPath, ["rev-parse", $"refs/heads/{branchName}"])).Trim();

	public async Task<string> GetCurrentBranchAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, ["branch", "--show-current"])).Trim();

	public async Task<string[]> GetRecentCommitMessagesAsync(int count)
	{
		var output = await RunGitAndCaptureOutputAsync(RootPath, ["log", $"-{count}", "--pretty=%B%x00"]);
		return output.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	public async Task<string> GetWorkingTreeStatusAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, ["status", "--porcelain", "--untracked-files=normal"])).Trim();

	public async Task<bool> HasBranchAsync(string branchName)
	{
		var result = await RunGitAndCaptureResultAsync(RootPath, ["show-ref", "--verify", "--quiet", $"refs/heads/{branchName}"]);
		return result.ExitCode == 0;
	}

	public async Task PushAsync(string remoteName, string branchName, bool setUpstream = false)
	{
		VerifyNotBare();
		if (setUpstream)
		{
			await RunGitAsync(RootPath, ["push", "-u", remoteName, branchName]);
			return;
		}

		await RunGitAsync(RootPath, ["push", remoteName, branchName]);
	}

	public async Task SwitchToBranchAsync(string branchName)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["switch", branchName]);
	}

	public async Task SwitchToNewBranchAsync(string branchName)
	{
		VerifyNotBare();
		await RunGitAsync(RootPath, ["switch", "-c", branchName]);
	}

	public void Dispose()
	{
		if (!Directory.Exists(RootPath))
			return;

		ClearAttributes(RootPath);

		for (var attempt = 0; attempt < 10; attempt++)
		{
			try
			{
				Directory.Delete(RootPath, recursive: true);
				return;
			}
			catch (IOException) when (attempt < 9)
			{
				Thread.Sleep(100);
			}
			catch (UnauthorizedAccessException) when (attempt < 9)
			{
				Thread.Sleep(100);
			}
		}
	}

	private void VerifyNotBare()
	{
		if (IsBare)
			throw new InvalidOperationException("This operation is not valid for a bare repository.");
	}

	private static void ClearAttributes(string rootPath)
	{
		foreach (var directoryPath in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
			File.SetAttributes(directoryPath, FileAttributes.Normal);

		foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
			File.SetAttributes(filePath, FileAttributes.Normal);

		File.SetAttributes(rootPath, FileAttributes.Normal);
	}

	private static async Task RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments)
	{
		var result = await RunGitAndCaptureResultAsync(workingDirectory, arguments);
		if (result.ExitCode != 0)
			throw new AssertionException($"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
	}

	private static async Task<string> RunGitAndCaptureOutputAsync(string workingDirectory, IReadOnlyList<string> arguments)
	{
		var result = await RunGitAndCaptureResultAsync(workingDirectory, arguments);
		if (result.ExitCode != 0)
			throw new AssertionException($"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");

		return result.StandardOutput;
	}

	private static async Task<GitCommandResult> RunGitAndCaptureResultAsync(string workingDirectory, IReadOnlyList<string> arguments)
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
		var standardOutput = await process.StandardOutput.ReadToEndAsync();
		var standardError = await process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();

		return new GitCommandResult(process.ExitCode, standardOutput, standardError);
	}

	private sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
}
