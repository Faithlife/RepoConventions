namespace RepoConventions;

internal readonly record struct ExternalCommandRunner(Func<string, string, string[], CancellationToken, Task<ExternalCommandResult>> RunAsyncDelegate)
{
	public Task<ExternalCommandResult> RunAsync(string fileName, string workingDirectory, string[] arguments, CancellationToken cancellationToken) =>
		RunAsyncDelegate(fileName, workingDirectory, arguments, cancellationToken);
}
