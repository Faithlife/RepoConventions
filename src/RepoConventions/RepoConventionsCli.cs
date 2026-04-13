using System.CommandLine;

namespace RepoConventions;

internal static class RepoConventionsCli
{
	public static async Task<int> InvokeAsync(string[] args, string workingDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
		=> await InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver: null, cancellationToken);

	internal static async Task<int> InvokeAsync(string[] args, string workingDirectory, TextWriter standardOutput, TextWriter standardError, Func<string, string, string>? remoteRepositoryUrlResolver, CancellationToken cancellationToken)
	{
		var commitOption = new Option<bool>("--commit")
		{
			Description = "Apply conventions and create commits as needed.",
		};
		var openPrOption = new Option<bool>("--open-pr")
		{
			Description = "Apply conventions, create commits, and open or update a pull request.",
		};

		var rootCommand = new RootCommand("Applies shared repository conventions.");
		rootCommand.Options.Add(commitOption);
		rootCommand.Options.Add(openPrOption);

		if (args.Length == 0)
			return await InvokeHelpAsync(rootCommand, standardOutput, standardError);

		var parseResult = rootCommand.Parse(args);
		if (parseResult.Errors.Count != 0)
			return await InvokeParseResultAsync(parseResult, standardOutput, standardError);

		if (!parseResult.GetValue(commitOption) && !parseResult.GetValue(openPrOption))
			return await InvokeHelpAsync(rootCommand, standardOutput, standardError);

		if (!await GitRepositoryValidator.IsRepositoryRootAsync(workingDirectory, cancellationToken))
		{
			await standardError.WriteLineAsync("The CLI must be run from the repository root.");
			return 1;
		}

		if (!await GitRepositoryValidator.IsCleanAsync(workingDirectory, cancellationToken))
		{
			await standardError.WriteLineAsync("The CLI must be run from a clean repository.");
			return 1;
		}

		var configPath = Path.Combine(workingDirectory, ".github", "conventions.yml");
		if (!File.Exists(configPath))
		{
			await standardError.WriteLineAsync("The conventions configuration file '.github/conventions.yml' was not found.");
			return 1;
		}

		var gitClient = new GitClient(workingDirectory, cancellationToken);
		var conventionRunner = new ConventionRunner(workingDirectory, gitClient, standardOutput, standardError, remoteRepositoryUrlResolver, cancellationToken);
		return await conventionRunner.RunAsync(configPath, parseResult.GetValue(openPrOption));
	}

	private static Task<int> InvokeHelpAsync(RootCommand rootCommand, TextWriter standardOutput, TextWriter standardError) =>
		InvokeParseResultAsync(rootCommand.Parse(["--help"]), standardOutput, standardError);

	private static async Task<int> InvokeParseResultAsync(ParseResult parseResult, TextWriter standardOutput, TextWriter standardError)
	{
		var originalOutput = Console.Out;
		var originalError = Console.Error;

		try
		{
			Console.SetOut(standardOutput);
			Console.SetError(standardError);
			return await parseResult.InvokeAsync(cancellationToken: CancellationToken.None);
		}
		finally
		{
			Console.SetOut(originalOutput);
			Console.SetError(originalError);
			await standardOutput.FlushAsync();
			await standardError.FlushAsync();
		}
	}

	private static class GitRepositoryValidator
	{
		public static async Task<bool> IsRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await GitClient.RunGitAsync(workingDirectory, cancellationToken, "rev-parse", "--show-toplevel");
			if (result.ExitCode != 0)
				return false;

			var repositoryRoot = result.StandardOutput.Trim();
			return Path.GetFullPath(repositoryRoot) == Path.GetFullPath(workingDirectory);
		}

		public static async Task<bool> IsCleanAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await GitClient.RunGitAsync(workingDirectory, cancellationToken, "status", "--porcelain", "--untracked-files=normal");
			return result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardOutput);
		}
	}
}
