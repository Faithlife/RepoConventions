using System.ComponentModel;
using System.Diagnostics;

namespace RepoConventions;

internal static class ProcessRunner
{
	public static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
	{
		try
		{
			await process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await TryKillProcessTreeAsync(process);
			throw;
		}
	}

	private static async Task TryKillProcessTreeAsync(Process process)
	{
		try
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);

			await process.WaitForExitAsync(CancellationToken.None);
		}
		catch (InvalidOperationException)
		{
		}
		catch (Win32Exception)
		{
		}
	}
}