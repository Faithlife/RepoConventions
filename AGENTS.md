# RepoConventions Agent Guidelines

- For C# analyzer or style issues, try `dotnet format` on the touched files or project before making manual formatting fixes.
- Prefer simple integration-style tests over mocks when the behavior can be exercised with temporary files, processes, or git repositories.
