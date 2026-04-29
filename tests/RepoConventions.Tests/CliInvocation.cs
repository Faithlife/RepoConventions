namespace RepoConventions.Tests;

internal static class CliInvocation
{
	public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver = null, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner = null, bool? useGitHubActionsGroupMarkers = null, CancellationToken cancellationToken = default)
	{
		using var standardOutput = new StringWriter();
		using var standardError = new StringWriter();

		var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, useGitHubActionsGroupMarkers, cancellationToken);

		return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
	}
}