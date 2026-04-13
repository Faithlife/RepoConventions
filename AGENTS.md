# RepoConventions Agent Guidelines

- For C# analyzer or style issues, try `dotnet format` on the touched files or project before making manual formatting fixes.
- For test visibility, prefer the modern csproj-based `InternalsVisibleTo` item syntax over `AssemblyInfo.cs` or generic assembly-attribute items.
- Prefer simple integration-style tests over mocks when the behavior can be exercised with temporary files, processes, or git repositories.
