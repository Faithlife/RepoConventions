using System.Text.Json;
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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath);

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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath);

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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Opening pull request from repo-conventions to main..."));
			Assert.That(result.StandardOutput, Does.Contain("Opened pull request: https://github.com/example/repo/pull/1"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(await origin.HasBranchAsync("repo-conventions"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("repo", "view"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--base"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("main"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--head"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("repo-conventions"));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("Apply repository conventions."));
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("[Conventions](https://github.com/example/repo/blob/repo-conventions/.github/conventions.yml) applied by [repo-conventions](https://github.com/Faithlife/RepoConventions):"));
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("[repo-conventions](https://github.com/Faithlife/RepoConventions)"));
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("[add-file](https://github.com/example/repo/tree/repo-conventions/.github/conventions/add-file)"));
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("add-file"));
		}
	}

	[Test]
	public async Task OpenPrModeLinksRemoteConventionsInPullRequestBody()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		var fakeGh = new FakeGitHubCli();
		remoteRepo.WriteFile("conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await remoteRepo.CommitAllAsync("Initial remote commit.");
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/add-file@main
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(
			["apply", "--open-pr"],
			repo.RootPath,
			remoteRepositoryUrlResolver: request =>
				request is { Owner: "local-test", Repository: "remote-conventions" }
					? remoteRepo.GetRepositoryUri()
					: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}."),
			externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(fakeGh.LastInvocation("pr", "create").Last(), Does.Contain("[add-file](https://github.com/local-test/remote-conventions/tree/main/conventions/add-file)"));
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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions-3"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
		}
	}

	[Test]
	public async Task OpenPrModeReusesExistingHigherSuffixPullRequestBranch()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/2", "repo-conventions-2", "main");
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		await repo.SwitchToNewBranchAsync("repo-conventions-2");
		await repo.PushAsync("origin", "repo-conventions-2", setUpstream: true);
		await repo.SwitchToBranchAsync("main");
		await repo.DeleteBranchAsync("repo-conventions-2");

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(result.StandardOutput, Does.Contain("Closed pull request: https://github.com/example/repo/pull/2"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions-2"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "comment").Last(), Is.EqualTo("No convention commits remain."));
			Assert.That(fakeGh.CountCalls("pr", "close"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeIgnoresExistingPullRequestForDifferentBaseBranch()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/3", "repo-conventions", "release");
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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Opening pull request from repo-conventions-2 to main..."));
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions-2"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("repo-conventions-2"));
		}
	}

	[Test]
	public async Task OpenPrModeFailsWhenMultipleConventionPullRequestsAreOpenForStartingBranch()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main");
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/2", "repo-conventions-2", "main");
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("Multiple repo-conventions pull requests are already open for main"));
			Assert.That(result.StandardError, Does.Contain("https://github.com/example/repo/pull/1"));
			Assert.That(result.StandardError, Does.Contain("https://github.com/example/repo/pull/2"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeReportsHelpfulMessageWhenMergeBaseCannotBeComputed()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main");
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		await repo.SwitchToNewOrphanBranchAsync("repo-conventions");
		repo.WriteFile("orphan.txt", "orphan");
		await repo.CommitAllAsync("Unrelated orphan commit.");
		await repo.SwitchToBranchAsync("main");

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("could not compare 'main' with existing pull request branch 'repo-conventions'"));
			Assert.That(result.StandardError, Does.Contain("git merge-base main repo-conventions failed."));
			Assert.That(result.StandardError, Does.Contain("Suggested fixes: fetch enough history for both branches"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeUpdatesExistingPullRequestBranchWithoutCreatingAnotherPullRequest()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main");
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

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(result.StandardOutput, Does.Contain("Updated pull request: https://github.com/example/repo/pull/1"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(await origin.HasBranchAsync("repo-conventions"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "comment").Last(), Does.Contain("added new convention commits"));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeRestartsOutOfDatePullRequestFromBaseAndForcePushesUpdatedCommits()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main");
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
		repo.WriteFile("created.txt", "created");
		await repo.CommitAllAsync("Apply convention add-file.");
		await repo.PushAsync("origin", "repo-conventions", setUpstream: true);
		await repo.SwitchToBranchAsync("main");
		repo.WriteFile("base.txt", "latest-base");
		await repo.CommitAllAsync("Advance base branch.");
		await repo.PushAsync("origin", "main");

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(result.StandardOutput, Does.Contain("Updated pull request: https://github.com/example/repo/pull/1"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(repo.FileExists("base.txt"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "comment").Last(), Does.Contain("force-pushed the updated convention commits"));
			Assert.That(fakeGh.CountCalls("pr", "close"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeFetchesExistingPullRequestBranchWhenRemoteTrackingRefIsMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main");
		repo.WriteFile(".github/conventions.yml", "conventions: []\n");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);
		await repo.SwitchToNewBranchAsync("repo-conventions");
		await repo.PushAsync("origin", "repo-conventions", setUpstream: true);
		await repo.SwitchToBranchAsync("main");
		await repo.DeleteBranchAsync("repo-conventions");
		await repo.DeleteRemoteTrackingBranchAsync("origin", "repo-conventions");

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(result.StandardOutput, Does.Contain("Closed pull request: https://github.com/example/repo/pull/1"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(fakeGh.CountCalls("pr", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "comment").Last(), Is.EqualTo("No convention commits remain."));
			Assert.That(fakeGh.CountCalls("pr", "close"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	private sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);

	private static class CliInvocation
	{
		public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver = null, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner = null)
		{
			var standardOutput = new StringWriter();
			var standardError = new StringWriter();

			var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, CancellationToken.None);

			return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
		}
	}

	private sealed class FakeGitHubCli
	{
		public Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>> Runner => RunAsync;

		public int PrCommentExitCode { get; set; }

		public int PrCloseExitCode { get; set; }

		public int PrCreateExitCode { get; set; }

		public string PrCreateOutput { get; set; } = "https://github.com/example/repo/pull/1";

		public string RepoViewOutput { get; set; } = "https://github.com/example/repo";

		private List<OpenPullRequest> OpenPullRequests { get; } = [];

		public List<string[]> Invocations { get; } = [];

		public int CountCalls(string command, string subcommand) => Invocations.Count(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public string[] LastInvocation(string command, string subcommand) => Invocations.Last(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public void AddOpenPullRequest(string url, string headRefName, string baseRefName) => OpenPullRequests.Add(new OpenPullRequest(url, headRefName, baseRefName));

		public Task<ExternalCommandResult> RunAsync(ExternalCommandRequest request, CancellationToken cancellationToken)
		{
			_ = cancellationToken;
			Assert.That(request.FileName, Is.EqualTo("gh"));
			Assert.That(request.WorkingDirectory, Is.Not.Empty);
			var arguments = request.Arguments;
			Invocations.Add([.. arguments]);

			if (arguments is ["pr", "list", ..])
				return Task.FromResult(new ExternalCommandResult(0, JsonSerializer.Serialize(OpenPullRequests, s_jsonSerializerOptions), ""));

			if (arguments is ["repo", "view", ..])
				return Task.FromResult(new ExternalCommandResult(0, RepoViewOutput, ""));

			if (arguments is ["pr", "comment", ..])
				return Task.FromResult(new ExternalCommandResult(PrCommentExitCode, "", PrCommentExitCode == 0 ? "" : "comment failed"));

			if (arguments is ["pr", "close", ..])
				return Task.FromResult(new ExternalCommandResult(PrCloseExitCode, "", PrCloseExitCode == 0 ? "" : "close failed"));

			if (arguments is ["pr", "create", ..])
				return Task.FromResult(new ExternalCommandResult(PrCreateExitCode, PrCreateOutput, PrCreateExitCode == 0 ? "" : "create failed"));

			return Task.FromResult(new ExternalCommandResult(1, "", $"Unexpected gh arguments: {string.Join(' ', arguments)}"));
		}

		private sealed record OpenPullRequest(string Url, string HeadRefName, string BaseRefName);

		private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};
	}
}
