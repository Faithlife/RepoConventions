using System.Diagnostics.CodeAnalysis;

namespace RepoConventions;

internal static class TemporaryPathFactory
{
	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Temporary paths only require short-lived collision avoidance, not cryptographic strength.")]
	public static string CreateDirectory(string rootPath)
	{
		var normalizedRootPath = EnsureRootDirectory(rootPath);
		while (true)
		{
			var path = CreateCandidatePath(normalizedRootPath, extension: null);
			try
			{
				Directory.CreateDirectory(path);
				return path;
			}
			catch (IOException) when (Path.Exists(path))
			{
			}
			catch (UnauthorizedAccessException ex)
			{
				throw new ProgramException($"Could not create a temporary directory under '{normalizedRootPath}': {ex.Message}", ex);
			}
		}
	}

	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Temporary paths only require short-lived collision avoidance, not cryptographic strength.")]
	public static string CreateFile(string rootPath, string extension)
	{
		var normalizedRootPath = EnsureRootDirectory(rootPath);
		while (true)
		{
			var path = CreateCandidatePath(normalizedRootPath, extension);
			try
			{
				using (File.Create(path))
				{
				}

				return path;
			}
			catch (IOException) when (Path.Exists(path))
			{
			}
			catch (UnauthorizedAccessException ex)
			{
				throw new ProgramException($"Could not create a temporary file under '{normalizedRootPath}': {ex.Message}", ex);
			}
		}
	}

	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Temporary paths only require short-lived collision avoidance, not cryptographic strength.")]
	private static string CreateCandidatePath(string rootPath, string? extension)
	{
		Span<byte> bytes = stackalloc byte[4];
		Random.Shared.NextBytes(bytes);
		var fileName = $"rc_{Convert.ToHexString(bytes).ToLowerInvariant()}{extension}";
		return Path.Combine(rootPath, fileName);
	}

	private static string EnsureRootDirectory(string rootPath)
	{
		try
		{
			return Directory.CreateDirectory(rootPath).FullName;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
		{
			throw new ProgramException($"Could not create the temporary root '{rootPath}': {ex.Message}", ex);
		}
	}
}
