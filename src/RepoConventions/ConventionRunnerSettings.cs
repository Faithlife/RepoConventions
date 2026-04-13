namespace RepoConventions;

internal sealed class ConventionRunnerSettings
{
	public required string TargetRepositoryRoot { get; init; }

	public required GitClient TargetGitClient { get; init; }

	public required TextWriter StandardOutput { get; init; }

	public required TextWriter StandardError { get; init; }

	public Func<RemoteRepositoryUrlRequest, string>? RemoteRepositoryUrlResolver { get; init; }

	public Func<ExternalCommandRequest, CancellationToken, Task<ExternalCommandResult>>? ExternalCommandRunner { get; init; }
}
