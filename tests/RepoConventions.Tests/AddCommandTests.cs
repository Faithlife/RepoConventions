using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class AddCommandTests
{
	[Test]
	public async Task AddModeCreatesConventionsFileWhenMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		WriteNoOpConvention(repo, ".github/conventions/add-file");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Added convention path './conventions/add-file'"));
			Assert.That(repo.FileExists(".github/conventions.yml"), Is.True);
			Assert.That(await repo.ReadFileAsync(".github/conventions.yml"), Does.Not.Contain("\r\n"));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
		}
	}

	[Test]
	public async Task AddModeAppendsConventionPathToExistingFileWithoutDroppingSettings()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			  - path: ./conventions/existing
			    settings:
			      enabled: true
			""");
		WriteNoOpConvention(repo, ".github/conventions/new");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/new"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_existingAndNewConventionPaths));
			Assert.That(references[0].Settings?["enabled"]?.GetValue<bool>(), Is.True);
		}
	}

	[Test]
	public async Task AddModeAddsMultipleConventionPaths()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		WriteNoOpConvention(repo, ".github/conventions/first");
		WriteNoOpConvention(repo, ".github/conventions/second");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/first", "./conventions/second"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Added convention path './conventions/first'"));
			Assert.That(result.StandardOutput, Does.Contain("Added convention path './conventions/second'"));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_multipleConventionPaths));
		}
	}

	[Test]
	public async Task AddModeDoesNotDuplicateExistingConventionPath()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			  - path: ./conventions/add-file
			""");
		var originalContents = await repo.ReadFileAsync(".github/conventions.yml");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file"], repo.RootPath);
		var updatedContents = await repo.ReadFileAsync(".github/conventions.yml");
		var references = ConventionConfiguration.Load(Path.Combine(repo.RootPath, ".github", "conventions.yml")).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("already present"));
			Assert.That(updatedContents, Is.EqualTo(originalContents));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
		}
	}

	[Test]
	public async Task AddModeFailsWhenNewConventionPathIsMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/missing"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("./conventions/missing"));
			Assert.That(repo.FileExists(".github/conventions.yml"), Is.False);
		}
	}

	[Test]
	public async Task AddModeLeavesExistingConfigUnchangedWhenNewConventionPathIsInvalid()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			  - path: ./conventions/existing
			""");
		WriteNoOpConvention(repo, ".github/conventions/new");
		var originalContents = await repo.ReadFileAsync(".github/conventions.yml");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/new", "./conventions/missing"], repo.RootPath);
		var updatedContents = await repo.ReadFileAsync(".github/conventions.yml");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("./conventions/missing"));
			Assert.That(updatedContents, Is.EqualTo(originalContents));
		}
	}

	[Test]
	public async Task AddModeValidatesRemoteConventionPath()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/add-file/convention.ps1", "# no-op\n");
		await remoteRepo.CommitAllAsync("Initial remote commit.");

		var result = await CliInvocation.InvokeAsync(
			["add", "local-test/remote-conventions/conventions/add-file@main"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));
		var references = ConventionConfiguration.Load(Path.Combine(repo.RootPath, ".github", "conventions.yml")).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(references.Select(static x => x.Path), Is.EqualTo(["local-test/remote-conventions/conventions/add-file@main"]));
		}
	}

	[Test]
	public async Task AddModePreservesCommentsWhenAppendingConventionPath()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			# leading comment
			pull-request:
			  auto-merge: true

			conventions:
			  # existing convention comment
			  - path: ./conventions/existing
			    settings:
			      enabled: true

			# trailing comment
			""");
		WriteNoOpConvention(repo, ".github/conventions/new");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/new"], repo.RootPath);
		var updatedContents = await repo.ReadFileAsync(".github/conventions.yml");

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(updatedContents, Does.Not.Contain("\r\n"));
			Assert.That(updatedContents, Does.Contain("# leading comment"));
			Assert.That(updatedContents, Does.Contain("# existing convention comment"));
			Assert.That(updatedContents, Does.Contain("# trailing comment"));
			Assert.That(updatedContents, Does.Contain("  - path: ./conventions/existing"));
			Assert.That(updatedContents, Does.Contain("    settings:"));
			Assert.That(updatedContents, Does.Contain("      enabled: true"));
			Assert.That(updatedContents, Does.Contain("  - path: ./conventions/new"));
			Assert.That(updatedContents.IndexOf("      enabled: true", StringComparison.Ordinal), Is.LessThan(updatedContents.IndexOf("  - path: ./conventions/new", StringComparison.Ordinal)));
			Assert.That(updatedContents.IndexOf("  - path: ./conventions/new", StringComparison.Ordinal), Is.LessThan(updatedContents.IndexOf("# trailing comment", StringComparison.Ordinal)));
		}
	}

	[Test]
	public async Task AddModeExpandsEmptyFlowConventionsSequenceWithoutDroppingOtherText()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			pull-request:
			  auto-merge: true
			conventions: [] # keep comment
			""");
		WriteNoOpConvention(repo, ".github/conventions/new");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/new"], repo.RootPath);
		var updatedContents = await repo.ReadFileAsync(".github/conventions.yml");
		var references = ConventionConfiguration.Load(Path.Combine(repo.RootPath, ".github", "conventions.yml")).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(updatedContents, Does.Contain("conventions:  # keep comment"));
			Assert.That(updatedContents, Does.Contain("  - path: ./conventions/new"));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(["./conventions/new"]));
		}
	}

	[Test]
	public async Task AddModeSucceedsEvenWhenRepositoryIsDirty()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		WriteNoOpConvention(repo, ".github/conventions/add-file");
		repo.WriteFile("untracked.txt", "content");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(repo.FileExists(".github/conventions.yml"), Is.True);
		}
	}

	[Test]
	public async Task AddCommitModeFailsWhenRepositoryIsDirty()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile("README.md", "test");
		await repo.CommitAllAsync("Initial commit.");
		repo.WriteFile("dirty.txt", "dirty");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--commit"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("clean repository"));
			Assert.That(repo.FileExists(".github/conventions.yml"), Is.False);
		}
	}

	[Test]
	public async Task AddCommitModeCommitsAddedConventionWithoutApplyingIt()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions/add-file/convention.ps1", "Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'\n");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--commit"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;
		var recentCommitMessages = await repo.GetRecentCommitMessagesAsync(2);
		var status = await repo.GetWorkingTreeStatusAsync();

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Added convention path './conventions/add-file'"));
			Assert.That(result.StandardOutput, Does.Not.Contain("Applying 1 conventions"));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
			Assert.That(recentCommitMessages[0], Is.EqualTo("Add convention ./conventions/add-file"));
			Assert.That(repo.FileExists("created.txt"), Is.False);
			Assert.That(status, Is.Empty);
		}
	}

	[Test]
	public async Task AddApplyModeFailsWhenRepositoryIsDirty()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile("README.md", "test");
		await repo.CommitAllAsync("Initial commit.");
		repo.WriteFile("dirty.txt", "dirty");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--apply"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("clean repository"));
			Assert.That(repo.FileExists(".github/conventions.yml"), Is.False);
		}
	}

	[Test]
	public async Task AddApplyModeCommitsAddedConventionAndAppliesItWithoutOpeningPullRequest()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions/add-file/convention.ps1", "Set-Content -Path (Join-Path $PWD 'created.txt') -Value 'created'\n");
		await repo.CommitAllAsync("Initial commit.");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--apply"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;
		var recentCommitMessages = await repo.GetRecentCommitMessagesAsync(3);
		var status = await repo.GetWorkingTreeStatusAsync();

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain("Added convention path './conventions/add-file'"));
			Assert.That(result.StandardOutput, Does.Contain("Applying 1 conventions"));
			Assert.That(result.StandardOutput, Does.Not.Contain("Opening pull request"));
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
			Assert.That(recentCommitMessages, Does.Contain("Add convention ./conventions/add-file"));
			Assert.That(recentCommitMessages, Does.Contain("Apply convention add-file"));
			Assert.That(repo.FileExists("created.txt"), Is.True);
			Assert.That(status, Is.Empty);
		}
	}

	[Test]
	public async Task AddModeSupportsRepoOptionOutsideRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		WriteNoOpConvention(repo, ".github/conventions/add-file");
		var launchDirectory = TemporaryDirectoryPath.Create();
		Directory.CreateDirectory(launchDirectory);

		try
		{
			var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--repo", repo.RootPath], launchDirectory);
			var configurationPath = Path.Combine(repo.RootPath, ".github", "conventions.yml");
			var references = ConventionConfiguration.Load(configurationPath).Conventions;

			using (Assert.EnterMultipleScope())
			{
				Assert.That(result.ExitCode, Is.Zero);
				Assert.That(result.StandardError, Is.Empty);
				Assert.That(result.StandardOutput, Does.Contain(".github/conventions.yml"));
				Assert.That(repo.FileExists(".github/conventions.yml"), Is.True);
				Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
			}
		}
		finally
		{
			Directory.Delete(launchDirectory, recursive: true);
		}
	}

	[Test]
	public async Task AddModeSupportsCustomConfigPath()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		WriteNoOpConvention(repo, ".config/conventions/add-file");

		var result = await CliInvocation.InvokeAsync(["add", "./conventions/add-file", "--config", ".config/repo-conventions.yml"], repo.RootPath);
		var configurationPath = Path.Combine(repo.RootPath, ".config", "repo-conventions.yml");
		var references = ConventionConfiguration.Load(configurationPath).Conventions;

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Does.Contain(".config/repo-conventions.yml"));
			Assert.That(result.StandardOutput, Does.Not.Contain("Added convention path './conventions/add-file' to '.github/conventions.yml'."));
			Assert.That(repo.FileExists(".config/repo-conventions.yml"), Is.True);
			Assert.That(references.Select(x => x.Path), Is.EqualTo(s_addFileConventionPaths));
		}
	}

	[Test]
	public async Task AddModeResolvesCustomConfigPathFromCurrentDirectory()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		var launchDirectory = TemporaryDirectoryPath.Create();
		Directory.CreateDirectory(launchDirectory);

		try
		{
			WriteNoOpConvention(repo, "conventions/add-file");

			var result = await CliInvocation.InvokeAsync(["add", "/conventions/add-file", "--repo", repo.RootPath, "--config", ".config/repo-conventions.yml"], launchDirectory);
			var configurationPath = Path.Combine(launchDirectory, ".config", "repo-conventions.yml");
			var references = ConventionConfiguration.Load(configurationPath).Conventions;

			using (Assert.EnterMultipleScope())
			{
				Assert.That(result.ExitCode, Is.Zero);
				Assert.That(result.StandardError, Is.Empty);
				Assert.That(result.StandardOutput, Does.Contain(".config/repo-conventions.yml"));
				Assert.That(File.Exists(configurationPath), Is.True);
				Assert.That(repo.FileExists(".config/repo-conventions.yml"), Is.False);
				Assert.That(references.Select(x => x.Path), Is.EqualTo(["/conventions/add-file"]));
			}
		}
		finally
		{
			Directory.Delete(launchDirectory, recursive: true);
		}
	}

	private static readonly string[] s_addFileConventionPaths = ["./conventions/add-file"];
	private static readonly string[] s_existingAndNewConventionPaths = ["./conventions/existing", "./conventions/new"];
	private static readonly string[] s_multipleConventionPaths = ["./conventions/first", "./conventions/second"];

	private static Func<RemoteRepositoryUrlRequest, string> LocalTestRemoteRepositoryUrlResolver(TemporaryGitRepository remoteRepo) =>
		request =>
			request is { Owner: "local-test", Repository: "remote-conventions" }
				? remoteRepo.GetRepositoryUri()
				: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}.");

	private static void WriteNoOpConvention(TemporaryGitRepository repo, string relativeDirectory) =>
		repo.WriteFile(Path.Combine(relativeDirectory, "convention.ps1"), "# no-op\n");
}
