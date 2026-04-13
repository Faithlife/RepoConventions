namespace RepoConventions;

internal delegate Task<ExternalCommandResult> ExternalCommandRunner(ExternalCommandRequest request, CancellationToken cancellationToken);
