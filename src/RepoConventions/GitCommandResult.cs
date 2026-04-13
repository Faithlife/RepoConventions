namespace RepoConventions;

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
