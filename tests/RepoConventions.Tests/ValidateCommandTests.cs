using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class ValidateCommandTests
{
	[Test]
	public async Task ValidateModeFailsWhenConfigIsMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain(".github/conventions.yml"));
		}
	}

	[Test]
	public async Task ValidateModeFailsWithFriendlyMessageWhenConfigYamlIsInvalid()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", "conventions:\n  - [\n");

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("not valid YAML"));
		}
	}

	[Test]
	public async Task ValidateModeSucceedsWithValidLocalScriptConventionWithoutRunningIt()
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

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Is.EqualTo("Validated 1 convention." + Environment.NewLine));
			Assert.That(repo.FileExists("created.txt"), Is.False);
		}
	}

	[Test]
	public async Task ValidateModeSucceedsWithValidCompositeConvention()
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
		repo.WriteFile(".github/conventions/child/convention.ps1", "# no-op\n");

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Is.EqualTo("Validated 2 conventions." + Environment.NewLine));
		}
	}

	[Test]
	public async Task ValidateModeFailsWhenLocalConventionDirectoryIsMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/missing
			""");

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("./conventions/missing"));
		}
	}

	[Test]
	public async Task ValidateModeFailsWhenLocalChildPathEscapesContainingRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		var repoParentPath = Directory.GetParent(repo.RootPath)!.FullName;
		var outsideConventionName = $"outside-{Guid.NewGuid():N}";
		var outsideConventionPath = Path.Combine(repoParentPath, outsideConventionName);
		Directory.CreateDirectory(outsideConventionPath);

		try
		{
			await File.WriteAllTextAsync(Path.Combine(outsideConventionPath, "convention.ps1"), "# no-op\n");
			repo.WriteFile(".github/conventions.yml", """
				conventions:
				- path: ./conventions/parent
				""");
			repo.WriteFile(".github/conventions/parent/convention.yml", $"conventions:{Environment.NewLine}- path: ../../../../{outsideConventionName}{Environment.NewLine}");

			var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

			using (Assert.EnterMultipleScope())
			{
				Assert.That(result.ExitCode, Is.Not.Zero);
				Assert.That(result.StandardError, Does.Contain("escapes the repository root"));
			}
		}
		finally
		{
			Directory.Delete(outsideConventionPath, recursive: true);
		}
	}

	[Test]
	public async Task ValidateModeFailsWhenConventionDirectoryContainsNoConventionFiles()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/empty
			""");
		repo.CreateDirectory(".github/conventions/empty");

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("did not contain convention.yml or convention.ps1"));
		}
	}

	[Test]
	public async Task ValidateModeSucceedsWithValidRemoteConventionPath()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/add-file/convention.ps1", "# no-op\n");
		await remoteRepo.CommitAllAsync("Initial remote commit.");
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/add-file@main
			""");

		var result = await CliInvocation.InvokeAsync(
			["validate"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Is.EqualTo("Validated 1 convention." + Environment.NewLine));
		}
	}

	[Test]
	public async Task ValidateModeFailsWhenRemoteCheckoutRefIsInvalid()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		using var remoteRepo = await TemporaryGitRepository.CreateAsync();
		remoteRepo.WriteFile("conventions/add-file/convention.ps1", "# no-op\n");
		await remoteRepo.CommitAllAsync("Initial remote commit.");
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: local-test/remote-conventions/conventions/add-file@missing
			""");

		var result = await CliInvocation.InvokeAsync(
			["validate"],
			repo.RootPath,
			LocalTestRemoteRepositoryUrlResolver(remoteRepo));

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Not.Zero);
			Assert.That(result.StandardError, Does.Contain("Failed to checkout ref 'missing'"));
		}
	}

	[Test]
	public async Task ValidateModeSucceedsWhenRepositoryIsDirty()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
		repo.WriteFile(".github/conventions.yml", """
			conventions:
			- path: ./conventions/add-file
			""");
		repo.WriteFile(".github/conventions/add-file/convention.ps1", "# no-op\n");
		repo.WriteFile("untracked.txt", "content");

		var result = await CliInvocation.InvokeAsync(["validate"], repo.RootPath);

		using (Assert.EnterMultipleScope())
		{
			Assert.That(result.ExitCode, Is.Zero);
			Assert.That(result.StandardError, Is.Empty);
			Assert.That(result.StandardOutput, Is.EqualTo("Validated 1 convention." + Environment.NewLine));
		}
	}

	private static Func<RemoteRepositoryUrlRequest, string> LocalTestRemoteRepositoryUrlResolver(TemporaryGitRepository remoteRepo) =>
		request =>
			request is { Owner: "local-test", Repository: "remote-conventions" }
				? remoteRepo.GetRepositoryUri()
				: throw new AssertionException($"Unexpected remote repository {request.Owner}/{request.Repository}.");
}
