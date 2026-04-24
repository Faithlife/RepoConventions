using NUnit.Framework;

namespace RepoConventions.Tests;

internal sealed class AddCommandTests
{
	[Test]
	public async Task AddModeCreatesConventionsFileWhenMissing()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();

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
	public async Task AddModeSupportsRepoOptionOutsideRepositoryRoot()
	{
		using var repo = await TemporaryGitRepository.CreateAsync();
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

	private static readonly string[] s_addFileConventionPaths = ["./conventions/add-file"];
	private static readonly string[] s_existingAndNewConventionPaths = ["./conventions/existing", "./conventions/new"];

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
