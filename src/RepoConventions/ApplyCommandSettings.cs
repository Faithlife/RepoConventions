namespace RepoConventions;

internal sealed record ApplyCommandSettings(bool OpenPullRequest, bool? AutoMerge, string? MergeMethod);
