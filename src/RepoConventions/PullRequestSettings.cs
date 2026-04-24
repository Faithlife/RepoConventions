namespace RepoConventions;

internal sealed record PullRequestSettings(
	IReadOnlyList<string>? Labels,
	IReadOnlyList<string>? Reviewers,
	IReadOnlyList<string>? Assignees,
	bool? Draft,
	bool? AutoMerge,
	string? MergeMethod);
