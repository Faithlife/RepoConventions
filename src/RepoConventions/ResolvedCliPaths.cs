namespace RepoConventions;

internal sealed class ResolvedCliPaths
{
	private ResolvedCliPaths(string currentDirectory, string repositoryRoot, string configurationPath, string tempRoot)
	{
		CurrentDirectory = currentDirectory;
		RepositoryRoot = repositoryRoot;
		ConfigurationPath = configurationPath;
		TempRoot = tempRoot;
	}

	public string ConfigurationDisplayPath => FormatPathForDisplay(ConfigurationPath);

	public string ConfigurationPath { get; }

	public string CurrentDirectory { get; }

	public string RepositoryRoot { get; }

	public string TempRoot { get; }

	public static ResolvedCliPaths Resolve(string currentDirectory, string? repositoryPath, string? configurationPath, string? tempPath)
	{
		try
		{
			var normalizedCurrentDirectory = Path.GetFullPath(currentDirectory);
			var repositoryRoot = string.IsNullOrWhiteSpace(repositoryPath)
				? normalizedCurrentDirectory
				: Path.GetFullPath(repositoryPath, normalizedCurrentDirectory);
			var resolvedConfigurationPath = string.IsNullOrWhiteSpace(configurationPath)
				? Path.Combine(repositoryRoot, ".github", "conventions.yml")
				: Path.GetFullPath(configurationPath, repositoryRoot);
			var resolvedTempRoot = string.IsNullOrWhiteSpace(tempPath)
				? Path.GetFullPath(Path.GetTempPath())
				: Path.GetFullPath(tempPath, repositoryRoot);

			return new ResolvedCliPaths(normalizedCurrentDirectory, repositoryRoot, resolvedConfigurationPath, resolvedTempRoot);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new ProgramException($"Failed to resolve CLI paths: {ex.Message}", ex);
		}
	}

	public string FormatPathForDisplay(string path)
	{
		var relativePath = Path.GetRelativePath(CurrentDirectory, path);
		if (!Path.IsPathRooted(relativePath))
			return NormalizePath(relativePath);

		return NormalizePath(path);
	}

	private static string NormalizePath(string path) => path.Replace(Path.DirectorySeparatorChar, '/');
}
