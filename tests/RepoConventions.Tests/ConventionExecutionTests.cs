using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class ConventionExecutionTests
{
	[Test]
	public async Task CommitModeFailsWhenConfigIsMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain(".github/conventions.yml"));
		}
	}

	[Test]
	public async Task CommitModeAppliesExecutableConventionAndCreatesCommit()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/add-file
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("created.txt"), Is.True);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention add-file."));
		}
	}

	[Test]
	public async Task CommitModePassesOnlySettingsToExecutableConvention()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/write-settings
			  settings:
			    greeting: hello
			    enabled: true
			""");
		repo.WriteFile(".github/conventions/write-settings/convention.ps1", """
			param([string] $configPath)
			$config = Get-Content -Raw $configPath | ConvertFrom-Json
			$output = @{ hasSettings = ($null -ne $config.settings); propertyCount = ($config.PSObject.Properties | Measure-Object).Count; greeting = $config.settings.greeting; enabled = $config.settings.enabled }
			$output | ConvertTo-Json -Compress | Set-Content -Path (Join-Path $PWD 'settings.json')
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.ReadFileAsync("settings.json"), Does.Contain("\"hasSettings\":true"));
			Assert.That(await repo.ReadFileAsync("settings.json"), Does.Contain("\"propertyCount\":1"));
			Assert.That(await repo.ReadFileAsync("settings.json"), Does.Contain("\"greeting\":\"hello\""));
		}
	}

	[Test]
	public async Task CommitModeSupportsRepositoryRootRelativeConventionPaths()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: /conventions/root-relative
			""");
		repo.WriteFile("conventions/root-relative/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'root-relative.txt') -Value 'created'
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("root-relative.txt"), Is.True);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention root-relative."));
		}
	}

	[Test]
	public async Task CommitModeFailsWithMissingConventionDirectoryMessage()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/use-slnx
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("directory"));
			Assert.That(result.StandardError, Does.Contain("./conventions/use-slnx"));
			Assert.That(result.StandardError, Does.Not.Contain("did not contain convention.yml or convention.ps1"));
		}
	}

	[Test]
	public async Task CommitModeAppliesCompositeConventionBeforeItsScript()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
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
			if (-not (Test-Path (Join-Path $PWD 'child.txt'))) { throw 'child missing' }
			Set-Content -Path (Join-Path $PWD 'parent.txt') -Value 'parent'
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("child.txt"), Is.True);
			Assert.That(repo.FileExists("parent.txt"), Is.True);
			Assert.That(await repo.GetRecentCommitMessagesAsync(2), Is.EqualTo(s_parentThenChildCommitMessages));
		}
	}

	[Test]
	public async Task CommitModeCleansUpFailedConventionWithoutRevertingPreviousCommit()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/good
			- path: ./conventions/bad
			""");
		repo.WriteFile(".github/conventions/good/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'good.txt') -Value 'good'
			""");
		repo.WriteFile(".github/conventions/bad/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'bad.txt') -Value 'bad'
			exit 1
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(repo.FileExists("good.txt"), Is.True);
			Assert.That(repo.FileExists("bad.txt"), Is.False);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention good."));
			Assert.That(await repo.GetWorkingTreeStatusAsync(), Is.Empty);
		}
	}

	[Test]
	public async Task CommitModeAppliesRemoteExecutableConventionAndCreatesCommit()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/add-file/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'remote.txt') -Value 'remote'
			""");
		await remoteRepo.CommitAllAsync("Initial remote commit.");

		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/add-file@main
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(
			["--commit"],
			repo.RootPath,
			request =>
				request is { Owner: "local-test", Repository: "remote-conventions" }
					? remoteRepo.GetRepositoryUri()
					: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}."));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("remote.txt"), Is.True);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention add-file."));
		}
	}

	[Test]
	public async Task CommitModeAppliesRemoteConventionAtRequestedRef()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/versioned/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'version.txt') -Value 'v1'
			""");
		await remoteRepo.CommitAllAsync("Version 1.");
		await remoteRepo.CreateTagAsync("v1");
		remoteRepo.WriteFile("conventions/versioned/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'version.txt') -Value 'main'
			""");
		await remoteRepo.CommitAllAsync("Version main.");

		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/versioned@v1
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(
			["--commit"],
			repo.RootPath,
			request =>
				request is { Owner: "local-test", Repository: "remote-conventions" }
					? remoteRepo.GetRepositoryUri()
					: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}."));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.ReadFileAsync("version.txt"), Does.Contain("v1"));
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention versioned."));
		}
	}

	[Test]
	public async Task CommitModeAppliesRemoteCompositeConventionBeforeItsScript()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/parent/convention.yml", """
			conventions:
			- path: ../child
			""");
		remoteRepo.WriteFile("conventions/child/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'remote-child.txt') -Value 'child'
			""");
		remoteRepo.WriteFile("conventions/parent/convention.ps1", """
			param([string] $configPath)
			if (-not (Test-Path (Join-Path $PWD 'remote-child.txt'))) { throw 'child missing' }
			Set-Content -Path (Join-Path $PWD 'remote-parent.txt') -Value 'parent'
			""");
		await remoteRepo.CommitAllAsync("Initial remote commit.");

		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/parent@main
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(
			["--commit"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("remote-child.txt"), Is.True);
			Assert.That(repo.FileExists("remote-parent.txt"), Is.True);
			Assert.That(await repo.GetRecentCommitMessagesAsync(2), Is.EqualTo(s_parentThenChildCommitMessages));
		}
	}

	[Test]
	public async Task CommitModeUsesRepositoryNameWhenRemoteConventionIsAtRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'remote-root.txt') -Value 'root'
			""");
		await remoteRepo.CommitAllAsync("Initial remote commit.");

		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions@main
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(
			["--commit"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("remote-root.txt"), Is.True);
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention remote-conventions."));
		}
	}

	[Test]
	public async Task CommitModeSkipsCycleDetectedAcrossRemoteReferences()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/cycle/convention.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/cycle@main
			""");
		remoteRepo.WriteFile("conventions/cycle/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'cycle.txt') -Value 'cycle'
			""");
		remoteRepo.WriteFile("conventions/after/convention.ps1", """
			param([string] $configPath)
			Set-Content -Path (Join-Path $PWD 'after.txt') -Value 'after'
			""");
		await remoteRepo.CommitAllAsync("Initial remote commit.");

		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/cycle@main
			- path: local-test/remote-conventions/conventions/after@main
			""");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(
			["--commit"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(repo.FileExists("cycle.txt"), Is.True);
			Assert.That(repo.FileExists("after.txt"), Is.True);
			Assert.That(result.StandardOutput, Does.Contain("skipped (cycle detected)"));
		}
	}

	private sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);

	private static class CliInvocation
	{
		public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver = null)
		{
			var standardOutput = new StringWriter();
			var standardError = new StringWriter();

			var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner: null, CancellationToken.None);

			return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
		}
	}

	private static Func<RemoteRepositoryUrlRequest, string> LocalTestRemoteRepositoryUrlResolver(TemporaryGitRepository remoteRepo) =>
		request =>
			request is { Owner: "local-test", Repository: "remote-conventions" }
				? remoteRepo.GetRepositoryUri()
				: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}.");

	private static readonly string[] s_parentThenChildCommitMessages = ["Apply convention parent.", "Apply convention child."];
}
