namespace RepoConventions;

internal sealed class ConventionRunnerSettings
{
	public required string TargetRepositoryRoot { get; init; }

	public required GitClient TargetGitClient { get; init; }

	public required TextWriter StandardOutput { get; init; }

	public required TextWriter StandardError { get; init; }

	public RemoteRepositoryUrlResolver? RemoteRepositoryUrlResolver { get; init; }

	public ExternalCommandRunner? ExternalCommandRunner { get; init; }
}
