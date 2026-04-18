using System.CommandLine;

namespace RepoConventions;

internal static class RepoConventionsCli
{
	public static async Task<int> InvokeAsync(string[] args, string workingDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
		=> await InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver: null, externalCommandRunner: null, cancellationToken);

	internal static async Task<int> InvokeAsync(string[] args, string workingDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, CancellationToken cancellationToken)
	{
		var openPrOption = new Option<bool>("--open-pr")
		{
			Description = "Apply conventions, create commits, and open or update a pull request.",
		};
		var conventionPathArgument = new Argument<string>("path")
		{
			Description = "Convention path to add to .github/conventions.yml.",
		};

		var rootCommand = new RootCommand("Applies shared repository conventions.");
		rootCommand.SetAction(_ => InvokeHelpAsync(rootCommand, standardOutput, standardError, cancellationToken));

		var applyCommand = new Command("apply", "Apply conventions and create commits as needed.");
		applyCommand.Options.Add(openPrOption);
		applyCommand.SetAction(parseResult =>
			ExecuteApplyAsync(
				parseResult,
				openPrOption,
				workingDirectory,
				standardOutput,
				standardError,
				remoteRepositoryUrlResolver,
				externalCommandRunner,
				cancellationToken));
		rootCommand.Subcommands.Add(applyCommand);

		var addCommand = new Command("add", "Add a convention path to the configuration file.");
		addCommand.Arguments.Add(conventionPathArgument);
		addCommand.SetAction(parseResult =>
			ExecuteAddAsync(
				parseResult,
				conventionPathArgument,
				workingDirectory,
				standardOutput,
				standardError,
				cancellationToken));
		rootCommand.Subcommands.Add(addCommand);

		var parseResult = rootCommand.Parse(args);
		return await InvokeParseResultAsync(parseResult, standardOutput, standardError, cancellationToken);
	}

	private static async Task<int> ExecuteApplyAsync(ParseResult parseResult, Option<bool> openPrOption, string workingDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, CancellationToken cancellationToken)
	{
		if (!await GitRepositoryValidator.IsRepositoryRootAsync(workingDirectory, cancellationToken))
		{
			await standardError.WriteLineAsync("repo-conventions must be run from the repository root.");
			return 1;
		}

		if (!await GitRepositoryValidator.IsCleanAsync(workingDirectory, cancellationToken))
		{
			await standardError.WriteLineAsync("repo-conventions must be run from a clean repository.");
			return 1;
		}

		var configPath = Path.Combine(workingDirectory, ".github", "conventions.yml");
		if (!File.Exists(configPath))
		{
			await standardError.WriteLineAsync("The conventions configuration file '.github/conventions.yml' was not found.");
			return 1;
		}

		var gitClient = new GitClient(workingDirectory);
		var conventionRunner = new ConventionRunner(new ConventionRunnerSettings
		{
			TargetRepositoryRoot = workingDirectory,
			TargetGitClient = gitClient,
			StandardOutput = standardOutput,
			StandardError = standardError,
			RemoteRepositoryUrlResolver = remoteRepositoryUrlResolver,
			ExternalCommandRunner = externalCommandRunner,
		});
		return await conventionRunner.RunAsync(configPath, parseResult.GetValue(openPrOption), cancellationToken);
	}

	private static async Task<int> ExecuteAddAsync(ParseResult parseResult, Argument<string> conventionPathArgument, string workingDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		if (!await GitRepositoryValidator.IsRepositoryRootAsync(workingDirectory, cancellationToken))
		{
			await standardError.WriteLineAsync("repo-conventions must be run from the repository root.");
			return 1;
		}

		var configPath = Path.Combine(workingDirectory, ".github", "conventions.yml");
		var conventionPath = parseResult.GetValue(conventionPathArgument) ?? throw new InvalidOperationException("Missing convention path.");
		if (ConventionConfiguration.AddConventionPath(configPath, conventionPath))
			await standardOutput.WriteLineAsync($"Added convention path '{conventionPath}' to '.github/conventions.yml'.");
		else
			await standardOutput.WriteLineAsync($"Convention path '{conventionPath}' is already present in '.github/conventions.yml'.");

		return 0;
	}

	private static Task<int> InvokeHelpAsync(RootCommand rootCommand, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken) =>
		InvokeParseResultAsync(rootCommand.Parse(["--help"]), standardOutput, standardError, cancellationToken);

	private static async Task<int> InvokeParseResultAsync(ParseResult parseResult, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		var originalOutput = Console.Out;
		var originalError = Console.Error;

		try
		{
			Console.SetOut(standardOutput);
			Console.SetError(standardError);
			return await parseResult.InvokeAsync(cancellationToken: cancellationToken);
		}
		finally
		{
			Console.SetOut(originalOutput);
			Console.SetError(originalError);
			await standardOutput.FlushAsync(cancellationToken);
			await standardError.FlushAsync(cancellationToken);
		}
	}

	private static class GitRepositoryValidator
	{
		public static async Task<bool> IsRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await GitClient.RunGitAsync(workingDirectory, ["rev-parse", "--show-prefix"], cancellationToken);
			if (result.ExitCode != 0)
				return false;

			return string.IsNullOrWhiteSpace(result.StandardOutput);
		}

		public static async Task<bool> IsCleanAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await GitClient.RunGitAsync(workingDirectory, ["status", "--porcelain", "--untracked-files=normal"], cancellationToken);
			return result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardOutput);
		}
	}
}
