namespace RepoConventions;

internal sealed record GitMergeBaseResult(string? MergeBase, GitCommandResult CommandResult, bool IsShallowRepository)
{
	public bool Succeeded => CommandResult.ExitCode == 0;

	public static GitMergeBaseResult Success(string mergeBase, GitCommandResult commandResult) => new(mergeBase, commandResult, IsShallowRepository: false);

	public static GitMergeBaseResult Failure(GitCommandResult commandResult, bool isShallowRepository) => new(null, commandResult, isShallowRepository);
}
