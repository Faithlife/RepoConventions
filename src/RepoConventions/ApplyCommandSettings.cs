namespace RepoConventions;

internal sealed record ApplyCommandSettings(bool OpenPullRequest, bool? Draft, bool? AutoMerge, string? MergeMethod, bool GitNoVerify);
