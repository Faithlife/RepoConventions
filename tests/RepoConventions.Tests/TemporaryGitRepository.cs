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

	public void Dispose()
	{
		if (Directory.Exists(RootPath))
			Directory.Delete(RootPath, recursive: true);
	}

	private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
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
	}
}
