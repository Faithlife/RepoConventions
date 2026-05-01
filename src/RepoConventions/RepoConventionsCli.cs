using System.CommandLine;

namespace RepoConventions;

internal static class RepoConventionsCli
{
	internal const int CanceledExitCode = 130;
	internal const string ShutdownRequestedMessage = "Shutdown requested.";

	public static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
		=> await InvokeAsync(args, currentDirectory, standardOutput, standardError, remoteRepositoryUrlResolver: null, externalCommandRunner: null, cancellationToken);

	internal static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, CancellationToken cancellationToken)
		=> await InvokeAsync(args, currentDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, useGitHubActionsGroupMarkers: null, GetGitHubStepSummaryPath(), cancellationToken);

	internal static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, bool? useGitHubActionsGroupMarkers, CancellationToken cancellationToken)
		=> await InvokeAsync(args, currentDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, useGitHubActionsGroupMarkers, GetGitHubStepSummaryPath(), cancellationToken);

	internal static async Task<int> InvokeAsync(string[] args, string currentDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, bool? useGitHubActionsGroupMarkers, string? gitHubStepSummaryPath, CancellationToken cancellationToken)
	{
		var shouldUseGitHubActionsGroupMarkers = useGitHubActionsGroupMarkers ?? IsRunningInGitHubActions();
		var openPrOption = new Option<bool>("--open-pr")
		{
			Description = "Apply conventions, create commits, and open or update a pull request.",
		};
		var draftOption = new Option<bool>("--draft")
		{
			Description = "Create the generated pull request as a draft.",
		};
		var noDraftOption = new Option<bool>("--no-draft")
		{
			Description = "Create the generated pull request as ready for review even if configuration enables draft mode.",
		};
		var autoMergeOption = new Option<bool>("--auto-merge")
		{
			Description = "Enable auto-merge for the generated pull request.",
		};
		var noAutoMergeOption = new Option<bool>("--no-auto-merge")
		{
			Description = "Disable auto-merge for this run even if configuration enables it.",
		};
		var mergeMethodOption = new Option<string>("--merge-method")
		{
			Description = "Preferred merge method for the generated pull request: merge, squash, or rebase.",
		};
		var gitNoVerifyOption = new Option<bool>("--git-no-verify")
		{
			Description = "Pass --no-verify to git commit and git push for repo-conventions-managed git operations.",
		};
		var conventionPathArgument = new Argument<string[]>("path")
		{
			Description = "Convention path or paths to add to the configuration file.",
			Arity = ArgumentArity.OneOrMore,
		};

		var rootCommand = new RootCommand("Applies shared repository conventions.");
		rootCommand.SetAction(_ => InvokeHelpAsync(rootCommand, standardOutput, standardError, cancellationToken));

		var applyCommand = new Command("apply", "Apply conventions and create commits as needed.");
		var applyRepoOption = new Option<string>("--repo")
		{
			Description = "Target repository root. Defaults to the current directory.",
		};
		var applyConfigOption = new Option<string>("--config")
		{
			Description = "Conventions configuration file path. Defaults to .github/conventions.yml under the repository root.",
		};
		var applyTempOption = new Option<string>("--temp")
		{
			Description = "Temporary root for RepoConventions-managed transient files. Defaults to the system temp directory.",
		};
		applyCommand.Options.Add(applyRepoOption);
		applyCommand.Options.Add(applyConfigOption);
		applyCommand.Options.Add(applyTempOption);
		applyCommand.Options.Add(openPrOption);
		applyCommand.Options.Add(draftOption);
		applyCommand.Options.Add(noDraftOption);
		applyCommand.Options.Add(autoMergeOption);
		applyCommand.Options.Add(noAutoMergeOption);
		applyCommand.Options.Add(mergeMethodOption);
		applyCommand.Options.Add(gitNoVerifyOption);
		applyCommand.SetAction(parseResult =>
			ExecuteApplyAsync(
				parseResult,
				applyRepoOption,
				applyConfigOption,
				applyTempOption,
				openPrOption,
				draftOption,
				noDraftOption,
				autoMergeOption,
				noAutoMergeOption,
				mergeMethodOption,
				gitNoVerifyOption,
				currentDirectory,
				standardOutput,
				standardError,
				remoteRepositoryUrlResolver,
				externalCommandRunner,
				shouldUseGitHubActionsGroupMarkers,
				gitHubStepSummaryPath,
				cancellationToken));
		rootCommand.Subcommands.Add(applyCommand);

		var addCommand = new Command("add", "Add a convention path to the configuration file.");
		var addRepoOption = new Option<string>("--repo")
		{
			Description = "Target repository root. Defaults to the current directory.",
		};
		var addConfigOption = new Option<string>("--config")
		{
			Description = "Conventions configuration file path. Defaults to .github/conventions.yml under the repository root.",
		};
		var addTempOption = new Option<string>("--temp")
		{
			Description = "Temporary root for RepoConventions-managed transient files. Defaults to the system temp directory.",
		};
		var addOpenPrOption = new Option<bool>("--open-pr")
		{
			Description = "Add conventions, apply conventions, create commits, and open or update a pull request.",
		};
		addCommand.Options.Add(addRepoOption);
		addCommand.Options.Add(addConfigOption);
		addCommand.Options.Add(addTempOption);
		addCommand.Options.Add(addOpenPrOption);
		addCommand.Options.Add(gitNoVerifyOption);
		addCommand.Arguments.Add(conventionPathArgument);
		addCommand.SetAction(parseResult =>
			ExecuteAddAsync(
				parseResult,
				addRepoOption,
				addConfigOption,
				addTempOption,
				addOpenPrOption,
				gitNoVerifyOption,
				conventionPathArgument,
				currentDirectory,
				standardOutput,
				standardError,
				remoteRepositoryUrlResolver,
				externalCommandRunner,
				shouldUseGitHubActionsGroupMarkers,
				gitHubStepSummaryPath,
				cancellationToken));
		rootCommand.Subcommands.Add(addCommand);

		var parseResult = rootCommand.Parse(args);
		return await InvokeParseResultAsync(parseResult, standardOutput, standardError, cancellationToken);
	}

	private static async Task<int> ExecuteApplyAsync(ParseResult parseResult, Option<string> repoOption, Option<string> configOption, Option<string> tempOption, Option<bool> openPrOption, Option<bool> draftOption, Option<bool> noDraftOption, Option<bool> autoMergeOption, Option<bool> noAutoMergeOption, Option<string> mergeMethodOption, Option<bool> gitNoVerifyOption, string currentDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, bool useGitHubActionsGroupMarkers, string? gitHubStepSummaryPath, CancellationToken cancellationToken)
	{
		try
		{
			var paths = ResolvedCliPaths.Resolve(currentDirectory, parseResult.GetValue(repoOption), parseResult.GetValue(configOption), parseResult.GetValue(tempOption));

			if (parseResult.GetValue(draftOption) && parseResult.GetValue(noDraftOption))
			{
				await standardError.WriteLineAsync("--draft and --no-draft cannot be used together.");
				return 1;
			}

			if (parseResult.GetValue(autoMergeOption) && parseResult.GetValue(noAutoMergeOption))
			{
				await standardError.WriteLineAsync("--auto-merge and --no-auto-merge cannot be used together.");
				return 1;
			}

			var mergeMethod = parseResult.GetValue(mergeMethodOption);
			if (mergeMethod is not null && mergeMethod is not ("merge" or "squash" or "rebase"))
			{
				await standardError.WriteLineAsync("--merge-method must be one of: merge, squash, rebase.");
				return 1;
			}

			if (!await GitRepositoryValidator.IsRepositoryRootAsync(paths.RepositoryRoot, cancellationToken))
			{
				await standardError.WriteLineAsync("repo-conventions must be run from the repository root.");
				return 1;
			}

			if (!await GitRepositoryValidator.IsCleanAsync(paths.RepositoryRoot, cancellationToken))
			{
				await standardError.WriteLineAsync("repo-conventions must be run from a clean repository.");
				return 1;
			}

			if (!File.Exists(paths.ConfigurationPath))
			{
				await standardError.WriteLineAsync($"The conventions configuration file '{paths.ConfigurationDisplayPath}' was not found.");
				return 1;
			}

			var gitClient = new GitClient(paths.RepositoryRoot);
			var conventionRunner = new ConventionRunner(new ConventionRunnerSettings
			{
				TargetRepositoryRoot = paths.RepositoryRoot,
				TargetGitClient = gitClient,
				TempRoot = paths.TempRoot,
				StandardOutput = standardOutput,
				StandardError = standardError,
				UseGitHubActionsGroupMarkers = useGitHubActionsGroupMarkers,
				GitHubStepSummaryPath = gitHubStepSummaryPath,
				RemoteRepositoryUrlResolver = remoteRepositoryUrlResolver,
				ExternalCommandRunner = externalCommandRunner,
			});
			bool? draft = parseResult.GetValue(draftOption)
				? true
				: parseResult.GetValue(noDraftOption)
					? false
					: null;
			bool? autoMerge = parseResult.GetValue(autoMergeOption)
				? true
				: parseResult.GetValue(noAutoMergeOption)
					? false
					: null;
			return await conventionRunner.RunAsync(paths.ConfigurationPath, new ApplyCommandSettings(parseResult.GetValue(openPrOption), draft, autoMerge, mergeMethod, parseResult.GetValue(gitNoVerifyOption)), cancellationToken);
		}
		catch (ProgramException ex)
		{
			await standardError.WriteLineAsync(ex.Message);
			return 1;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return await WriteCancellationMessageAsync(standardError);
		}
	}

	private static async Task<int> ExecuteAddAsync(ParseResult parseResult, Option<string> repoOption, Option<string> configOption, Option<string> tempOption, Option<bool> openPrOption, Option<bool> gitNoVerifyOption, Argument<string[]> conventionPathArgument, string currentDirectory, TextWriter standardOutput, TextWriter standardError, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner, bool useGitHubActionsGroupMarkers, string? gitHubStepSummaryPath, CancellationToken cancellationToken)
	{
		try
		{
			var paths = ResolvedCliPaths.Resolve(currentDirectory, parseResult.GetValue(repoOption), parseResult.GetValue(configOption), parseResult.GetValue(tempOption));

			if (!await GitRepositoryValidator.IsRepositoryRootAsync(paths.RepositoryRoot, cancellationToken))
			{
				await standardError.WriteLineAsync("repo-conventions must be run from the repository root.");
				return 1;
			}

			var openPullRequest = parseResult.GetValue(openPrOption);
			if (openPullRequest && !await GitRepositoryValidator.IsCleanAsync(paths.RepositoryRoot, cancellationToken))
			{
				await standardError.WriteLineAsync("repo-conventions must be run from a clean repository.");
				return 1;
			}

			var conventionPaths = parseResult.GetValue(conventionPathArgument) ?? throw new InvalidOperationException("Missing convention path.");
			var gitClient = new GitClient(paths.RepositoryRoot);
			var conventionRunner = new ConventionRunner(new ConventionRunnerSettings
			{
				TargetRepositoryRoot = paths.RepositoryRoot,
				TargetGitClient = gitClient,
				TempRoot = paths.TempRoot,
				StandardOutput = standardOutput,
				StandardError = standardError,
				UseGitHubActionsGroupMarkers = useGitHubActionsGroupMarkers,
				GitHubStepSummaryPath = gitHubStepSummaryPath,
				RemoteRepositoryUrlResolver = remoteRepositoryUrlResolver,
				ExternalCommandRunner = externalCommandRunner,
			});

			return await conventionRunner.AddAsync(paths.ConfigurationPath, paths.ConfigurationDisplayPath, conventionPaths, openPullRequest, parseResult.GetValue(gitNoVerifyOption), cancellationToken);
		}
		catch (ProgramException ex)
		{
			await standardError.WriteLineAsync(ex.Message);
			return 1;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return await WriteCancellationMessageAsync(standardError);
		}
	}

	private static Task<int> InvokeHelpAsync(RootCommand rootCommand, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken) =>
		InvokeParseResultAsync(rootCommand.Parse(["--help"]), standardOutput, standardError, cancellationToken);

	private static async Task<int> InvokeParseResultAsync(ParseResult parseResult, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
	{
		var invocationConfiguration = new InvocationConfiguration
		{
			Output = standardOutput,
			Error = standardError,
		};

		try
		{
			return await parseResult.InvokeAsync(invocationConfiguration, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return await WriteCancellationMessageAsync(standardError);
		}
		finally
		{
			await standardOutput.FlushAsync(CancellationToken.None);
			await standardError.FlushAsync(CancellationToken.None);
		}
	}

	private static async Task<int> WriteCancellationMessageAsync(TextWriter standardError)
	{
		await standardError.WriteLineAsync(ShutdownRequestedMessage);
		return CanceledExitCode;
	}

	private static bool IsRunningInGitHubActions() =>
		string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

	private static string? GetGitHubStepSummaryPath()
	{
		var path = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
		return string.IsNullOrWhiteSpace(path) ? null : path;
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
