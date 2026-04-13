using System.Diagnostics;
using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class GitRepoTests
{
	[Test]
	public async Task CommitModeSucceedsFromCleanRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
		}
	}

	[Test]
	public async Task CommitModeFailsOutsideRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		var nestedDirectory = repo.CreateDirectory("src");

		var result = await CliInvocation.InvokeAsync(["--commit"], nestedDirectory);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("repository root"));
		}
	}

	[Test]
	public async Task CommitModeFailsWhenRepositoryIsDirty()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile("untracked.txt", "content");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("clean repository"));
		}
	}

	private sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);

	private static class CliInvocation
	{
		public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory)
		{
			var standardOutput = new StringWriter();
			var standardError = new StringWriter();

			var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, CancellationToken.None);

			return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
		}
	}

	private sealed class TemporaryGitRepository : IDisposable
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
}
