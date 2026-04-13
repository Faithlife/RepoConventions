namespace RepoConventions;

internal readonly record struct ExternalCommandResult(int ExitCode, string StandardOutput, string StandardError);
