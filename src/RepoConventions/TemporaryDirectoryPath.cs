using System.Diagnostics.CodeAnalysis;

namespace RepoConventions;

internal static class TemporaryDirectoryPath
{
	[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Temporary directory names only require short-lived collision avoidance, not cryptographic strength.")]
	public static string Create()
	{
		while (true)
		{
			var path = Path.Combine(Path.GetTempPath(), $"rc_{Random.Shared.GetHexString(8, true)}");
			if (!Path.Exists(path))
				return path;
		}
	}
}
