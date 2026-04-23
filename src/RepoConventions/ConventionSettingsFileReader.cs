using System.Text;

namespace RepoConventions;

internal static class ConventionSettingsFileReader
{
	public static string ReadText(ConventionSettingsEvaluationContext context, string path)
	{
		var repositoryRoot = Path.GetFullPath(context.RepositoryRoot);
		var resolvedPath = ResolvePath(context, path, repositoryRoot);
		if (!File.Exists(resolvedPath))
			throw new InvalidOperationException($"File '{resolvedPath}' was not found.");

		try
		{
			var bytes = File.ReadAllBytes(resolvedPath);
			var offset = HasUtf8Bom(bytes) ? 3 : 0;
			return s_utf8NoBom.GetString(bytes, offset, bytes.Length - offset);
		}
		catch (DecoderFallbackException ex)
		{
			throw new InvalidOperationException($"File '{resolvedPath}' is not valid UTF-8 text.", ex);
		}
	}

	private static string ResolvePath(ConventionSettingsEvaluationContext context, string path, string repositoryRoot)
	{
		if (IsNativeAbsolutePath(path))
			throw new InvalidOperationException($"Native absolute path '{path}' is not allowed.");

		var combinedPath = path.StartsWith('/')
			? Path.Combine(repositoryRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
			: Path.Combine(context.ConfigurationDirectory, path);

		var resolvedPath = Path.GetFullPath(combinedPath);
		if (!IsWithinRepositoryRoot(resolvedPath, repositoryRoot))
			throw new InvalidOperationException($"Resolved path '{resolvedPath}' escapes the repository root '{repositoryRoot}'.");

		return resolvedPath;
	}

	private static bool HasUtf8Bom(byte[] bytes) =>
		bytes.Length >= 3
		&& bytes[0] == 0xEF
		&& bytes[1] == 0xBB
		&& bytes[2] == 0xBF;

	private static bool IsNativeAbsolutePath(string path) =>
		path.Length > 0
		&& !path.StartsWith('/')
		&& Path.IsPathRooted(path);

	private static bool IsWithinRepositoryRoot(string path, string repositoryRoot)
	{
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var normalizedRoot = Path.TrimEndingDirectorySeparator(repositoryRoot);
		if (string.Equals(path, normalizedRoot, comparison))
			return true;

		return path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
			|| path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison);
	}

	private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
