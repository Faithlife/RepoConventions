using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class GitRepoTests
{
	[Test]
	public async Task CommitModeSucceedsFromCleanRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");

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
}
