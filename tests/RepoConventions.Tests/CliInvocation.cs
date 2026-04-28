namespace RepoConventions.Tests;

internal static class CliInvocation
{
	public static async Task<CliInvocationResult> InvokeAsync(string[] args, string workingDirectory, Func<RemoteRepositoryUrlRequest, string>? remoteRepositoryUrlResolver = null, Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? externalCommandRunner = null, bool? useGitHubActionsGroupMarkers = null)
	{
		var standardOutput = new StringWriter();
		var standardError = new StringWriter();

		var exitCode = await RepoConventionsCli.InvokeAsync(args, workingDirectory, standardOutput, standardError, remoteRepositoryUrlResolver, externalCommandRunner, useGitHubActionsGroupMarkers, CancellationToken.None);

		return new CliInvocationResult(exitCode, standardOutput.ToString(), standardError.ToString());
	}
}