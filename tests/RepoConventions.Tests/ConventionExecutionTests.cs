using NUnit.Framework;

namespace RepoConventions.Tests;

[NonParallelizable]
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
			- path: octocat/conventions/conventions/add-file@main
			""");
		await repo.CommitAllAsync("Initial commit.");

		using var remoteRepositoryUrlResolverScope = UseRemoteRepositoryUrlResolver((owner, repository) =>
			owner == "octocat" && repository == "conventions"
				? remoteRepo.GetRepositoryUri()
				: throw new AssertionException($"Unexpected remote repository {owner}/{repository}."));

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

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
			- path: octocat/conventions/conventions/versioned@v1
			""");
		await repo.CommitAllAsync("Initial commit.");

		using var remoteRepositoryUrlResolverScope = UseRemoteRepositoryUrlResolver((owner, repository) =>
			owner == "octocat" && repository == "conventions"
				? remoteRepo.GetRepositoryUri()
				: throw new AssertionException($"Unexpected remote repository {owner}/{repository}."));

		var result = await CliInvocation.InvokeAsync(["--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(await repo.ReadFileAsync("version.txt"), Does.Contain("v1"));
			Assert.That(await repo.GetHeadCommitMessageAsync(), Is.EqualTo("Apply convention versioned."));
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

	private static DisposableAction UseRemoteRepositoryUrlResolver(Func<string, string, string> resolver)
	{
		var previous = ConventionRunner.RemoteRepositoryUrlResolver;
		ConventionRunner.RemoteRepositoryUrlResolver = resolver;
		return new DisposableAction(() => ConventionRunner.RemoteRepositoryUrlResolver = previous);
	}

	private sealed class DisposableAction(Action dispose) : IDisposable
	{
		public void Dispose() => dispose();
	}

	private static readonly string[] s_parentThenChildCommitMessages = ["Apply convention parent.", "Apply convention child."];
}
