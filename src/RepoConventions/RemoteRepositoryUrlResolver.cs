namespace RepoConventions;

internal readonly record struct RemoteRepositoryUrlResolver(Func<string, string, string> Resolve)
{
	public string GetRepositoryUrl(string owner, string repository) => Resolve(owner, repository);
}
