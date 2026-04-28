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

		var result = await CliInvocation.InvokeAsync(["apply"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
		}
	}

	[Test]
	public async Task GitClientCommitAllAsyncReturnsFalseWhenNothingIsStaged()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile("tracked.txt", "content");
		await repo.CommitAllAsync("Initial commit.");

		var gitClient = new GitClient(repo.RootPath);
		var committed = await gitClient.CommitAllAsync("Apply convention no-op", CancellationToken.None);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(committed, Is.False);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Initial commit."));
			Assert.That(await repo.GetWorkingTreeStatusAsync(), Is.Empty);
		}
	}

	[Test]
	public async Task CommitModeFailsOutsideRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		var nestedDirectory = repo.CreateDirectory("src");

		var result = await CliInvocation.InvokeAsync(["apply"], nestedDirectory);

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

		var result = await CliInvocation.InvokeAsync(["apply"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("clean repository"));
		}
	}

	[Test]
	public async Task VersionOptionPrintsVersionInsteadOfHelp()
	{
		var result = await CliInvocation.InvokeAsync(["--version"], Environment.CurrentDirectory);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput.Trim(), Does.Match(@"^\d+\.\d+\.\d+"));
		}
	}

	[Test]
	public async Task HelpOutputListsCommandsInsteadOfCommitOption()
	{
		var result = await CliInvocation.InvokeAsync(["--help"], Environment.CurrentDirectory);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("add"));
			Assert.That(result.StandardOutput, Does.Contain("apply"));
			Assert.That(result.StandardOutput, Does.Not.Contain("--commit"));
		}
	}
}
