using System.Diagnostics.CodeAnalysis;

namespace RepoConventions;

internal static class TemporaryDirectoryPath
{
	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Temporary directory names only require short-lived collision avoidance, not cryptographic strength.")]
	public static string Create()
	{
		Span<byte> bytes = stackalloc byte[4];
		while (true)
		{
			Random.Shared.NextBytes(bytes);
			var path = Path.Combine(Path.GetTempPath(), $"rc_{Convert.ToHexString(bytes).ToLowerInvariant()}");
			if (!Path.Exists(path))
				return path;
		}
	}
}
