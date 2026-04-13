using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class OpenPrTests
{
	[Test]
	public async Task OpenPrModeFailsWhenHeadIsDetached()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.DetachHeadAsync();

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("detached HEAD"));
		}
	}

	[Test]
	public async Task OpenPrModeFailsWhenRepositoryHasUnpushedCommits()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		repo.WriteFile("unpushed.txt", "unpushed");
		await repo.CommitAllAsync("Unpushed commit.");

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("no unpushed commits"));
		}
	}

	[Test]
	public async Task OpenPrModeCreatesConventionBranchPushesItAndCreatesPullRequest()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/add-file
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(await origin.HasBranchAsync("repo-conventions"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--base"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("main"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--head"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("repo-conventions"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("Apply repository conventions"));
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("add-file"));
		}
	}

	[Test]
	public async Task OpenPrModeDoesNotPushOrCreatePullRequestWhenNoChangesAreMade()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(await origin.HasBranchAsync("repo-conventions"), Is.False);
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeChoosesNextAvailableConventionBranchAcrossLocalAndRemoteBranches()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		await repo.CreateBranchAsync("repo-conventions");
		await repo.SwitchToNewBranchAsync("repo-conventions-2");
		await repo.PushAsync("origin", "repo-conventions-2", setUpstream: true);
		await repo.SwitchToBranchAsync("main");
		await repo.DeleteBranchAsync("repo-conventions-2");

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions-3"));
			Assert.That(fakeGh.ListHeadsQueried, Is.EqualTo(["repo-conventions", "repo-conventions-2", "repo-conventions-3"]));
		}
	}

	[Test]
	public async Task OpenPrModeUpdatesExistingPullRequestBranchWithoutCreatingAnotherPullRequest()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.PrListOutput = /*lang=json,strict*/ "[{\"number\":1}]";
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/add-file
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		await repo.SwitchToNewBranchAsync("repo-conventions");
		await repo.PushAsync("origin", "repo-conventions", setUpstream: true);
		await repo.SwitchToBranchAsync("main");
		await repo.DeleteBranchAsync("repo-conventions");

		var result = await CliInvocation.InvokeAsync(["--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(await origin.HasBranchAsync("repo-conventions"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	private sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);

	private static class CliInvocation
	{
		public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory, RemoteRepositoryUrlResolver? remoteRepositoryUrlResolver = null, ExternalCommandRunner? externalCommandRunner = null)
		{
			var standardOutput = new StringWriter();
			var standardError = new StringWriter();

			var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, CancellationToken.None);

			return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
		}
	}

	private sealed class FakeGitHubCli
	{
		public ExternalCommandRunner Runner => new(RunAsync);

		public int PrCreateExitCode { get; set; }

		public string PrCreateOutput { get; set; } = "https://example.invalid/pr/1";

		public string PrListOutput { get; set; } = "[]";

		public List<string> ListHeadsQueried { get; } = [];

		public List<string[]> Invocations { get; } = [];

		public int CountCalls(string command, string subcommand) => Invocations.Count(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public string[] LastInvocation(string command, string subcommand) => Invocations.Last(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public Task<ExternalCommandResult> RunAsync(string fileName, string workingDirectory, string[] arguments, CancellationToken cancellationToken)
		{
			_ = cancellationToken;
			Assert.That(fileName, Is.EqualTo("gh"));
			Assert.That(workingDirectory, Is.Not.Empty);
			Invocations.Add([.. arguments]);

			if (arguments is ["pr", "list", ..])
			{
				for (var index = 0; index < arguments.Length - 1; index++)
				{
					if (arguments[index] == "--head")
						ListHeadsQueried.Add(arguments[index + 1]);
				}

				return Task.FromResult(new ExternalCommandResult(0, PrListOutput, ""));
			}

			if (arguments is ["pr", "create", ..])
				return Task.FromResult(new ExternalCommandResult(PrCreateExitCode, PrCreateOutput, PrCreateExitCode == 0 ? "" : "create failed"));

			return Task.FromResult(new ExternalCommandResult(1, "", $"Unexpected gh arguments: {string.Join(' ', arguments)}"));
		}
	}
}
