using System.CommandLine;
using System.Diagnostics;

namespace RepoConventions;

internal static class RepoConventionsCli
{
	public static async Task<int> InvokeAsync(string[] args, string workingDirectory, TextWriter standardOutput, TextWriter standardError, CancellationToken cancellationToken)
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
			return await InvokeHelpAsync(rootCommand, standardOutput, standardError).ConfigureAwait(false);

		var parseResult = rootCommand.Parse(args);
		if (parseResult.Errors.Count != 0)
			return await InvokeParseResultAsync(parseResult, standardOutput, standardError).ConfigureAwait(false);

		if (!parseResult.GetValue(commitOption) && !parseResult.GetValue(openPrOption))
			return await InvokeHelpAsync(rootCommand, standardOutput, standardError).ConfigureAwait(false);

		if (!await GitRepositoryValidator.IsRepositoryRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
		{
			await standardError.WriteLineAsync("The CLI must be run from the repository root.").ConfigureAwait(false);
			return 1;
		}

		if (!await GitRepositoryValidator.IsCleanAsync(workingDirectory, cancellationToken).ConfigureAwait(false))
		{
			await standardError.WriteLineAsync("The CLI must be run from a clean repository.").ConfigureAwait(false);
			return 1;
		}

		return 0;
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
			return await parseResult.InvokeAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
		}
		finally
		{
			Console.SetOut(originalOutput);
			Console.SetError(originalError);
			await standardOutput.FlushAsync().ConfigureAwait(false);
			await standardError.FlushAsync().ConfigureAwait(false);
		}
	}

	private static class GitRepositoryValidator
	{
		public static async Task<bool> IsRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await RunGitAsync(workingDirectory, cancellationToken, "rev-parse", "--show-toplevel").ConfigureAwait(false);
			if (result.ExitCode != 0)
				return false;

			var repositoryRoot = result.StandardOutput.Trim();
			return Path.GetFullPath(repositoryRoot) == Path.GetFullPath(workingDirectory);
		}

		public static async Task<bool> IsCleanAsync(string workingDirectory, CancellationToken cancellationToken)
		{
			var result = await RunGitAsync(workingDirectory, cancellationToken, "status", "--porcelain", "--untracked-files=normal").ConfigureAwait(false);
			return result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardOutput);
		}

		private static async Task<GitResult> RunGitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
		{
			var startInfo = new ProcessStartInfo("git")
			{
				WorkingDirectory = workingDirectory,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
			};

			foreach (var argument in arguments)
				startInfo.ArgumentList.Add(argument);

			using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
			var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

			return new GitResult(
				process.ExitCode,
				await standardOutputTask.ConfigureAwait(false),
				await standardErrorTask.ConfigureAwait(false));
		}

		private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
	}
}
