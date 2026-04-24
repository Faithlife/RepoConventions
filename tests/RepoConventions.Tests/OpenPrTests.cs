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
	public async Task OpenPrModeCreatesMissingLabelsAndAppliesConfiguredMetadata()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			pull-request:
			  labels:
			    - automation
			  reviewers:
			    - octocat
			    - my-org/build-team
			  assignees:
			    - octocat
			conventions:
			- path: ./conventions/add-file
			  pull-request:
			    labels:
			      - dependencies
			    reviewers:
			      - my-org/dotnet-team
			    assignees:
			      - hubot
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);
		var createInvocation = fakeGh.LastInvocation("pr", "create");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(fakeGh.CountCalls("label", "list"), Is.EqualTo(1));
			Assert.That(fakeGh.CountCalls("label", "create"), Is.EqualTo(3));
			Assert.That(result.StandardOutput, Does.Contain("Opened pull request: https://github.com/example/repo/pull/1 (labels: automation, dependencies; reviewers: octocat, my-org/build-team, my-org/dotnet-team; assignees: octocat, hubot)"));
			Assert.That(result.StandardOutput, Does.Not.Contain("labels: repo-conventions"));
			Assert.That(createInvocation, Does.Contain("--label"));
			Assert.That(createInvocation, Does.Contain("repo-conventions"));
			Assert.That(createInvocation, Does.Contain("automation"));
			Assert.That(createInvocation, Does.Contain("dependencies"));
			Assert.That(createInvocation, Does.Contain("--reviewer"));
			Assert.That(createInvocation, Does.Contain("octocat"));
			Assert.That(createInvocation, Does.Contain("my-org/build-team"));
			Assert.That(createInvocation, Does.Contain("my-org/dotnet-team"));
			Assert.That(createInvocation, Does.Contain("--assignee"));
			Assert.That(createInvocation, Does.Contain("hubot"));
		}
	}

	[Test]
	public async Task OpenPrModeCreatesDraftPullRequestWhenRepositorySettingRequiresIt()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			pull-request:
			  draft: true
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
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--draft"));
		}
	}

	[Test]
	public async Task OpenPrModeCreatesDraftPullRequestWhenContributingConventionRequestsIt()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/add-file
			  pull-request:
			    draft: true
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
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Contain("--draft"));
		}
	}

	[Test]
	public async Task OpenPrModeIgnoresDraftFromConventionThatCreatesNoCommits()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/no-op
			  pull-request:
			    draft: true
			- path: ./conventions/add-file
			""");
		repo.WriteFile(".github/conventions/no-op/convention.ps1", """
			param([string] $configPath)
			Write-Output 'no changes'
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
			Assert.That(fakeGh.LastInvocation("pr", "create"), Does.Not.Contain("--draft"));
		}
	}

	[Test]
	public async Task OpenPrModeSkipsReviewersAndAssigneesWhenAutoMergeIsEnabled()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			pull-request:
			  reviewers:
			    - octocat
			  assignees:
			    - hubot
			  auto-merge: true
			  merge-method: squash
			conventions:
			- path: ./conventions/add-file
			  pull-request:
			    reviewers:
			      - my-org/dotnet-team
			    assignees:
			      - octocat
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);
		var createInvocation = fakeGh.LastInvocation("pr", "create");
		var mergeInvocation = fakeGh.LastInvocation("pr", "merge");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(createInvocation, Does.Not.Contain("--reviewer"));
			Assert.That(createInvocation, Does.Not.Contain("--assignee"));
			Assert.That(mergeInvocation, Does.Contain("--squash"));
			Assert.That(result.StandardOutput, Does.Contain("Opened pull request: https://github.com/example/repo/pull/1 (auto-merge, squash)"));
			Assert.That(result.StandardOutput, Does.Not.Contain("reviewers:"));
			Assert.That(result.StandardOutput, Does.Not.Contain("assignees:"));
		}
	}

	[Test]
	public async Task OpenPrModeFallsBackToSquashWhenDesiredMergeMethodIsNotAllowed()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli { RebaseMergeAllowed = false };
		repo.WriteFile(".github/conventions.yml", """
			pull-request:
			  auto-merge: true
			  merge-method: rebase
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
		var mergeInvocation = fakeGh.LastInvocation("pr", "merge");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "merge"), Is.EqualTo(1));
			Assert.That(mergeInvocation, Does.Contain("--squash"));
			Assert.That(mergeInvocation, Does.Not.Contain("--rebase"));
			Assert.That(result.StandardOutput, Does.Contain("Opened pull request: https://github.com/example/repo/pull/1 (auto-merge, squash, rebase disabled)"));
		}
	}

	[Test]
	public async Task OpenPrModeOnlyUsesConventionMetadataFromConventionsThatCreateCommits()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/no-op
			  pull-request:
			    labels:
			      - ignored-label
			    reviewers:
			      - ignored-reviewer
			- path: ./conventions/add-file
			  pull-request:
			    labels:
			      - applied-label
			""");
		repo.WriteFile(".github/conventions/no-op/convention.ps1", """
			param([string] $configPath)
			Write-Output 'no changes'
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);
		var createInvocation = fakeGh.LastInvocation("pr", "create");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(createInvocation, Does.Contain("applied-label"));
			Assert.That(createInvocation, Does.Not.Contain("ignored-label"));
			Assert.That(createInvocation, Does.Not.Contain("ignored-reviewer"));
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
	public async Task OpenPrModeSupportsRepoAndCustomConfigPaths()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		var launchDirectory = TemporaryDirectoryPath.Create();
		Directory.CreateDirectory(launchDirectory);
		repo.WriteFile(".config/repo-conventions.yml", """
			conventions:
			- path: /conventions/add-file
			""");
		repo.WriteFile("conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		try
		{
			var result = await CliInvocation.InvokeAsync(["apply", "--open-pr", "--repo", repo.RootPath, "--config", ".config/repo-conventions.yml"], launchDirectory, externalCommandRunner: fakeGh.Runner);
			var body = fakeGh.LastInvocation("pr", "create").Last();

			using (Assert.EnterMultipleScope())
			{
				Assert.That(result.ExitCode, Is.Zero);
				Assert.That(body, Does.Contain("[Conventions](https://github.com/example/repo/blob/repo-conventions/.config/repo-conventions.yml) applied by [repo-conventions](https://github.com/Faithlife/RepoConventions):"));
				Assert.That(body, Does.Contain("[add-file](https://github.com/example/repo/tree/repo-conventions/conventions/add-file)"));
			}
		}
		finally
		{
			Directory.Delete(launchDirectory, recursive: true);
		}
	}

	[Test]
	public async Task OpenPrModeUsesNestedBulletsForNestedConventionsInPullRequestBody()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/parent
			""");
		repo.WriteFile(".github/conventions/parent/convention.yml", """
			conventions:
			- path: ../child
			""");
		repo.WriteFile(".github/conventions/child/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'child.txt') -Value 'child'
			""");
		repo.WriteFile(".github/conventions/parent/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'parent.txt') -Value 'parent'
			""");
		await repo.CommitAllAsync("Initial commit.");
		await repo.AddRemoteAsync("origin", origin.RootPath);
		await repo.PushAsync("origin", "main", setUpstream: true);

		var result = await CliInvocation.InvokeAsync(["apply", "--open-pr"], repo.RootPath, externalCommandRunner: fakeGh.Runner);
		var body = fakeGh.LastInvocation("pr", "create").Last();

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(body, Does.Contain("- [parent](https://github.com/example/repo/tree/repo-conventions/.github/conventions/parent)"));
			Assert.That(body, Does.Contain("\n  - [child](https://github.com/example/repo/tree/repo-conventions/.github/conventions/child)"));
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
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request"));
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
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main", body: BuildSingleConventionPullRequestBody("repo-conventions", "add-file", ".github/conventions/add-file"));
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
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "edit"), Is.EqualTo(1));
			Assert.That(fakeGh.LastInvocation("pr", "edit"), Does.Contain("--add-label"));
			Assert.That(fakeGh.LastInvocation("pr", "edit"), Does.Contain("repo-conventions"));
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeRestartsOutOfDatePullRequestFromBaseAndForcePushesUpdatedCommits()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main", body: "Outdated pull request body");
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
		var editInvocations = fakeGh.Invocations.Where(static x => x.Length >= 2 && x[0] == "pr" && x[1] == "edit").ToList();

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Not.Contain("Pull request is already open:"));
			Assert.That(result.StandardOutput, Does.Contain("Updated pull request: https://github.com/example/repo/pull/1"));
			Assert.That(await repo.GetCurrentBranchAsync(), Is.EqualTo("repo-conventions"));
			Assert.That(repo.FileExists("base.txt"), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "edit"), Is.EqualTo(2));
			Assert.That(editInvocations.Any(static x => x.Any(static y => y == "--body") && x.Last().Contains("[add-file](https://github.com/example/repo/tree/repo-conventions/.github/conventions/add-file)", StringComparison.Ordinal)), Is.True);
			Assert.That(editInvocations.Any(static x => x.Any(static y => y == "--add-label") && x.Any(static y => y == "repo-conventions")), Is.True);
			Assert.That(fakeGh.CountCalls("pr", "close"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "create"), Is.Zero);
		}
	}

	[Test]
	public async Task OpenPrModeUpdatesExistingPullRequestBodyWhenItChanges()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var origin = await TemporaryGitRepository.CreateBareAsync();
		var fakeGh = new FakeGitHubCli();
		fakeGh.AddOpenPullRequest("https://github.com/example/repo/pull/1", "repo-conventions", "main", body: "Old body");
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
		var editInvocations = fakeGh.Invocations.Where(static x => x.Length >= 2 && x[0] == "pr" && x[1] == "edit").ToList();

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "comment"), Is.Zero);
			Assert.That(fakeGh.CountCalls("pr", "edit"), Is.EqualTo(2));
			Assert.That(editInvocations.Any(static x => x.Any(static y => y == "--body") && x.Last().Contains("[add-file](https://github.com/example/repo/tree/repo-conventions/.github/conventions/add-file)", StringComparison.Ordinal)), Is.True);
			Assert.That(editInvocations.Any(static x => x.Any(static y => y == "--add-label") && x.Any(static y => y == "repo-conventions")), Is.True);
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

	private static string BuildSingleConventionPullRequestBody(string branchName, string conventionName, string conventionPath) => string.Join(
		Environment.NewLine,
		$"[Conventions](https://github.com/example/repo/blob/{branchName}/.github/conventions.yml) applied by [repo-conventions](https://github.com/Faithlife/RepoConventions):",
		$"- [{conventionName}](https://github.com/example/repo/tree/{branchName}/{conventionPath})");

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

		public bool AutoMergeAllowed { get; set; } = true;

		public int PrCommentExitCode { get; set; }

		public int PrEditExitCode { get; set; }

		public int PrCloseExitCode { get; set; }

		public int PrCreateExitCode { get; set; }

		public int PrMergeExitCode { get; set; }

		public string PrMergeOutput { get; set; } = "";

		public string PrMergeError { get; set; } = "";

		public string PrCreateOutput { get; set; } = "https://github.com/example/repo/pull/1";

		public string RepoViewOutput { get; set; } = "https://github.com/example/repo";

		public bool MergeCommitAllowed { get; set; } = true;

		public bool RebaseMergeAllowed { get; set; } = true;

		public bool SquashMergeAllowed { get; set; } = true;

		private List<OpenPullRequest> OpenPullRequests { get; } = [];

		private HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

		public List<string[]> Invocations { get; } = [];

		public int CountCalls(string command, string subcommand) => Invocations.Count(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public string[] LastInvocation(string command, string subcommand) => Invocations.Last(x => x.Length >= 2 && x[0] == command && x[1] == subcommand);

		public void AddOpenPullRequest(string url, string headRefName, string baseRefName, string body = "") => OpenPullRequests.Add(new OpenPullRequest(url, headRefName, baseRefName, body));

		public void AddExistingLabel(string name) => Labels.Add(name);

		public Task<ExternalCommandResult> RunAsync(ExternalCommandRequest request, CancellationToken cancellationToken)
		{
			_ = cancellationToken;
			Assert.That(request.FileName, Is.EqualTo("gh"));
			Assert.That(request.WorkingDirectory, Is.Not.Empty);
			var arguments = request.Arguments;
			Invocations.Add([.. arguments]);

			if (arguments is ["pr", "list", ..])
				return Task.FromResult(new ExternalCommandResult(0, JsonSerializer.Serialize(OpenPullRequests, s_jsonSerializerOptions), ""));

			if (arguments is ["label", "list", ..])
				return Task.FromResult(new ExternalCommandResult(0, JsonSerializer.Serialize(Labels.Select(static x => new LabelRecord(x)), s_jsonSerializerOptions), ""));

			if (arguments is ["label", "create", var label, ..])
			{
				Labels.Add(label);
				return Task.FromResult(new ExternalCommandResult(0, "", ""));
			}

			if (arguments is ["repo", "view", ..])
			{
				if (arguments.Contains("--jq"))
					return Task.FromResult(new ExternalCommandResult(0, RepoViewOutput, ""));

				return Task.FromResult(new ExternalCommandResult(0, JsonSerializer.Serialize(new RepoViewRecord(RepoViewOutput, AutoMergeAllowed, MergeCommitAllowed, RebaseMergeAllowed, SquashMergeAllowed), s_jsonSerializerOptions), ""));
			}

			if (arguments is ["pr", "comment", ..])
				return Task.FromResult(new ExternalCommandResult(PrCommentExitCode, "", PrCommentExitCode == 0 ? "" : "comment failed"));

			if (arguments is ["pr", "edit", ..])
				return Task.FromResult(new ExternalCommandResult(PrEditExitCode, "", PrEditExitCode == 0 ? "" : "edit failed"));

			if (arguments is ["pr", "close", ..])
				return Task.FromResult(new ExternalCommandResult(PrCloseExitCode, "", PrCloseExitCode == 0 ? "" : "close failed"));

			if (arguments is ["pr", "create", ..])
				return Task.FromResult(new ExternalCommandResult(PrCreateExitCode, PrCreateOutput, PrCreateExitCode == 0 ? "" : "create failed"));

			if (arguments is ["pr", "merge", ..])
				return Task.FromResult(new ExternalCommandResult(PrMergeExitCode, PrMergeOutput, PrMergeExitCode == 0 ? PrMergeError : (string.IsNullOrEmpty(PrMergeError) ? "merge failed" : PrMergeError)));

			return Task.FromResult(new ExternalCommandResult(1, "", $"Unexpected gh arguments: {string.Join(' ', arguments)}"));
		}

		private sealed record LabelRecord(string Name);

		private sealed record OpenPullRequest(string Url, string HeadRefName, string BaseRefName, string Body);

		private sealed record RepoViewRecord(string Url, bool AutoMergeAllowed, bool MergeCommitAllowed, bool RebaseMergeAllowed, bool SquashMergeAllowed);

		private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};
	}
}
