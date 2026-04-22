# RepoConventions Agent Guidelines

- For C# analyzer or style issues, try `dotnet format` on the touched files or project before making manual formatting fixes.
- Keep C# files to one top-level type each. When adding a new class, record, interface, enum, or struct, put it in its own file instead of appending it to an existing file.
- Place private fields and constants at the bottom of C# classes.
- Keep raw C# multiline strings consistently indented to match the surrounding code.
- For YAML deserialization in C#, prefer converting YAML to JSON first and then using `System.Text.Json`.
- For test visibility, prefer the modern csproj-based `InternalsVisibleTo` item syntax over `AssemblyInfo.cs` or generic assembly-attribute items.
- Prefer simple integration-style tests over mocks when the behavior can be exercised with temporary files, processes, or git repositories.
