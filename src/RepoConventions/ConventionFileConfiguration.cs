namespace RepoConventions;

internal sealed record ConventionFileConfiguration(IReadOnlyList<ConventionReference> Conventions, PullRequestSettings? PullRequest);
