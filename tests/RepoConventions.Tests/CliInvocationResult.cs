namespace RepoConventions.Tests;

internal sealed record CliInvocationResult(int ExitCode, string StandardOutput, string StandardError);