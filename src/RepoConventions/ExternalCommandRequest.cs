namespace RepoConventions;

internal readonly record struct ExternalCommandRequest(string FileName, string WorkingDirectory, IReadOnlyList<string> Arguments);
