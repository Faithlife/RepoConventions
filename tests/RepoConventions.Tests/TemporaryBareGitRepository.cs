using System.Diagnostics;
using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class TemporaryBareGitRepository : IDisposable
{
	private TemporaryBareGitRepository(string rootPath) => RootPath = rootPath;

	public string RootPath { get; }

	public static async Task<TemporaryBareGitRepository> CreateAsync()
	{
		var rootPath = Path.Combine(Path.GetTempPath(), $"RepoConventions.Tests.Bare.{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootPath);
		await RunGitAsync(Path.GetTempPath(), "init", "--bare", rootPath);
		return new TemporaryBareGitRepository(rootPath);
	}

	public async Task<bool> HasBranchAsync(string branchName)
	{
		var result = await RunGitAndCaptureResultAsync(RootPath, "show-ref", "--verify", "--quiet", $"refs/heads/{branchName}");
		return result.ExitCode == 0;
	}

	public async Task<string> GetHeadCommitShaAsync(string branchName)
	{
		var result = await RunGitAndCaptureResultAsync(RootPath, "rev-parse", $"refs/heads/{branchName}");
		if (result.ExitCode != 0)
			throw new AssertionException($"git rev-parse refs/heads/{branchName} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");

		return result.StandardOutput.Trim();
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
		var result = await RunGitAndCaptureResultAsync(workingDirectory, arguments);
		if (result.ExitCode != 0)
			throw new AssertionException($"git {string.Join(' ', arguments)} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
	}

	private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAndCaptureResultAsync(string workingDirectory, params string[] arguments)
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

		return (process.ExitCode, standardOutput, standardError);
	}
}
