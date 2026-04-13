using System.Diagnostics;
using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class TemporaryGitRepository : IDisposable
{
	private TemporaryGitRepository(string rootPath) => RootPath = rootPath;

	public string RootPath { get; }

	public static async Task<TemporaryGitRepository> CreateAsync()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Tests.{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootPath);

		await RunGitAsync(rootPath, "init", "--initial-branch=main");
		await RunGitAsync(rootPath, "config", "user.name", "RepoConventions Tests");
		await RunGitAsync(rootPath, "config", "user.email", "repo-conventions-tests@example.com");

		return new TemporaryGitRepository(rootPath);
	}

	public string CreateDirectory(string relativePath)
	{
		var fullPath = Path.Combine(RootPath, relativePath);
		Directory.CreateDirectory(fullPath);
		return fullPath;
	}

	public void WriteFile(string relativePath, string contents)
	{
		var fullPath = Path.Combine(RootPath, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		File.WriteAllText(fullPath, contents);
	}

	public bool FileExists(string relativePath) => File.Exists(Path.Combine(RootPath, relativePath));

	public string GetRepositoryUri() => new Uri(RootPath + Path.DirectorySeparatorChar).AbsoluteUri;

	public Task<string> ReadFileAsync(string relativePath) => File.ReadAllTextAsync(Path.Combine(RootPath, relativePath));

	public async Task CommitAllAsync(string message)
	{
		await RunGitAsync(RootPath, "add", "-A");
		await RunGitAsync(RootPath, "commit", "-m", message);
	}

	public async Task CreateTagAsync(string tagName) => await RunGitAsync(RootPath, "tag", tagName);

	public async Task<string> GetHeadCommitMessageAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, "log", "-1", "--pretty=%B")).Trim();

	public async Task<string[]> GetRecentCommitMessagesAsync(int count)
	{
		var output = await RunGitAndCaptureOutputAsync(RootPath, "log", $"-{count}", "--pretty=%B%x00");
		return output.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	public async Task<string> GetWorkingTreeStatusAsync() =>
		(await RunGitAndCaptureOutputAsync(RootPath, "status", "--porcelain", "--untracked-files=normal")).Trim();

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

	private static void ClearAttributes(string rootPath)
	{
		foreach (var directoryPath in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
			File.SetAttributes(directoryPath, FileAttributes.Normal);

		foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
			File.SetAttributes(filePath, FileAttributes.Normal);

		File.SetAttributes(rootPath, FileAttributes.Normal);
	}

	private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
	{
		_ = await RunGitAndCaptureOutputAsync(workingDirectory, arguments);
	}

	private static async Task<string> RunGitAndCaptureOutputAsync(string workingDirectory, params string[] arguments)
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

		if (process.ExitCode != 0)
			throw new AssertionException($"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{standardError}");

		return standardOutput;
	}
}
