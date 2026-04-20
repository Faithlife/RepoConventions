namespace RepoConventions;

internal static class TemporaryDirectoryPath
{
	public static string Create()
	{
		while (true)
		{
#pragma warning disable CA5394 // Temp directory names only require short-lived collision avoidance, not cryptographic strength.
			var path = Path.Combine(Path.GetTempPath(), $"rc_{Random.Shared.GetHexString(8, true)}");
#pragma warning restore CA5394
			if (!Directory.Exists(path) && !File.Exists(path))
				return path;
		}
	}
}
