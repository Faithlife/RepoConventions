namespace RepoConventions;

internal readonly record struct ConventionRunnerSettings(
	string TargetRepositoryRoot,
	GitClient TargetGitClient,
	TextWriter StandardOutput,
	TextWriter StandardError,
	RemoteRepositoryUrlResolver? RemoteRepositoryUrlResolver = null,
	ExternalCommandRunner? ExternalCommandRunner = null);
