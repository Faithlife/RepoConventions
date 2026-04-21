# PowerShell Convention Script Functions

## Status

Proposed.

## Purpose

Executable conventions currently receive a JSON input file and then implement their own PowerShell helpers inline. That keeps the runtime model simple, but it also pushes each convention author to repeatedly solve the same problems:

- parse the JSON input file
- validate required settings
- read and write repository files safely
- report useful status to the console
- decide whether a change is needed before writing files

This document outlines ideas for exposing a small set of commonly used PowerShell functions to `convention.ps1` scripts so convention authors can focus on convention logic instead of boilerplate.

## Goals

- Reduce repeated PowerShell boilerplate across convention scripts.
- Keep executable conventions easy to author and easy to understand.
- Preserve the current execution model where `convention.ps1` runs in the target repository root.
- Make common idempotent file updates straightforward.
- Provide a clear path for settings access and validation.
- Keep helper behavior predictable enough that conventions remain debuggable.

## Non-Goals

- Replacing PowerShell with a different scripting model.
- Hiding all file system behavior behind a large abstraction layer.
- Introducing a second configuration file format for executable conventions.
- Making convention scripts depend on network access or external modules.
- Solving every repository mutation scenario in the first version.

## Current Model

Today, `repo-conventions` runs `convention.ps1` with one argument: the path to a JSON file containing a `settings` object. The script runs from the root of the target Git repository and can directly inspect or modify files in that repository.

That contract is intentionally small, but authors of executable conventions still need to write the same setup code repeatedly:

```pwsh
param([string] $configPath)

$config = Get-Content -Raw $configPath | ConvertFrom-Json
$settings = $config.settings
```

That pattern is simple, but common conventions also tend to need helpers for tasks such as:

- requiring a setting and failing with a useful error message
- resolving a path relative to the repository root
- updating a text file only when the content actually changed
- ensuring a line or block exists in a file
- serializing structured data with stable formatting

## Proposed Direction

Expose a small built-in PowerShell helper surface to each `convention.ps1` script.

The most promising shape is:

- `repo-conventions` writes or locates a helper script next to the generated JSON input.
- `repo-conventions` launches a lightweight wrapper that dot-sources the helper script, then invokes the convention script.
- The convention script still receives the same JSON input path and still runs from the repository root.
- Authors can call helper functions directly without manually importing a module from the convention directory.

This keeps the current contract mostly intact while allowing a shared function library to evolve over time.

## Candidate Helper Areas

### Settings helpers

These functions remove repeated JSON parsing and validation code.

Ideas:

- `Get-ConventionSettings`
- `Get-ConventionSetting -Name <name>`
- `Get-ConventionSetting -Path <dotted.path>`
- `Require-ConventionSetting -Name <name>`
- `Test-ConventionSetting -Name <name>`

Example:

```pwsh
$licenseHeader = Require-ConventionSetting -Name 'licenseHeader'
$nullable = Get-ConventionSetting -Path 'dotnet.nullable'
```

Notes:

- The helper should cache the parsed input instead of reading the JSON file repeatedly.
- Missing required settings should produce a concise terminating error that identifies the convention and setting name.
- The helper should not silently coerce types beyond what `ConvertFrom-Json` already produces.

### Repository path helpers

These functions make path handling less error-prone when scripts are executed from the repository root.

Ideas:

- `Get-RepositoryRoot`
- `Resolve-RepositoryPath -Path <relativeOrAbsolutePath>`
- `Test-RepositoryPath -Path <path>`
- `Get-RepositoryRelativePath -Path <absolutePath>`

Example:

```pwsh
$propsPath = Resolve-RepositoryPath 'Directory.Build.props'
```

Notes:

- Helpers should reject paths that escape the repository root when the intent is to operate on repo content.
- This surface should stay small; PowerShell already provides strong generic path primitives.

### Text file helpers

Many conventions are fundamentally text transformations. A small set of idempotent helpers would remove a large amount of repetition.

Ideas:

- `Get-TextFile -Path <path>`
- `Set-TextFileIfChanged -Path <path> -Content <content>`
- `Ensure-FileContains -Path <path> -Value <text>`
- `Ensure-LinePresent -Path <path> -Line <line>`
- `Replace-InFile -Path <path> -OldValue <text> -NewValue <text>`
- `Update-TextFile -Path <path> -Transform <scriptblock>`

Example:

```pwsh
Update-TextFile -Path 'README.md' -Transform {
    param($content)
    $content -replace 'old-text', 'new-text'
}
```

Notes:

- Writes should be skipped when the resulting content is byte-for-byte identical.
- UTF-8 without BOM is the safest default unless an existing file indicates a different encoding should be preserved.
- Helpers should avoid adding trailing whitespace or extra final newlines unexpectedly.

### Structured file helpers

Some conventions repeatedly touch JSON, YAML, XML, or MSBuild files.

Ideas:

- `Get-JsonFile` and `Set-JsonFileIfChanged`
- `Get-XmlFile` and `Set-XmlFileIfChanged`
- `Get-MsBuildProject` and `Save-MsBuildProjectIfChanged`

These helpers should be added cautiously. They are useful, but they also introduce formatting and round-trip risks. The initial version may be better if it focuses on text helpers first and adds structured-file helpers only after a few real conventions show clear demand.

### Logging helpers

Convention authors need concise output without re-creating their own formatting conventions.

Ideas:

- `Write-ConventionInfo <message>`
- `Write-ConventionWarning <message>`
- `Write-ConventionChange <message>`
- `Write-ConventionError <message>`

Notes:

- Logging helpers should remain thin wrappers over standard PowerShell output.
- They should not hide failures or invent a separate diagnostics pipeline.
- If the CLI later emits GitHub Actions group markers or machine-readable logs, these helpers provide a stable authoring surface.

### Assertion helpers

A few small assertion helpers could make scripts more explicit.

Ideas:

- `Assert-FileExists -Path <path>`
- `Assert-CommandAvailable -Name <command>`
- `Assert-Setting -Name <name>`

These should fail fast with short, convention-focused messages.

## Delivery Options

### Option 1: Dot-sourced helper script injected by the CLI

`repo-conventions` creates a temporary helper script and launches PowerShell in a way that dot-sources that helper before invoking `convention.ps1`.

Pros:

- No packaging story for convention authors.
- Easy for the CLI to version along with its runtime.
- Keeps the convention script authoring experience simple.

Cons:

- The invocation path is slightly more complex than today's direct script call.
- Debugging the exact startup sequence may be less obvious.

### Option 2: Built-in module imported by the wrapper

`repo-conventions` ships a PowerShell module and imports it before invoking the convention script.

Pros:

- Cleaner organization of exported helper functions.
- Easier to document as a real module surface.

Cons:

- Requires more care around module loading and versioning.
- Slightly more ceremony than a simple helper script.

### Option 3: Convention authors opt in by dot-sourcing a known helper path

The CLI would expose a helper path, and each script would import it explicitly.

Pros:

- Minimal change to the execution flow.
- Makes the dependency visible in each script.

Cons:

- Convention authors still write boilerplate in every script.
- Easy to forget, which weakens the value of shared helpers.

## Recommendation

Start with Option 1.

It gives the best balance of simplicity and author ergonomics:

- executable conventions still feel like plain PowerShell scripts
- the helper surface can ship with the CLI and evolve in lockstep
- scripts automatically get the helper functions without extra import code

The first helper set should stay intentionally small:

- settings helpers
- repository path helpers
- text file helpers
- logging helpers

Structured file helpers should wait until there are concrete conventions that justify their complexity.

## Function Naming Guidance

Prefer verb-noun names that read like normal PowerShell commands.

Suggested naming rules:

- Use `Get-`, `Set-`, `Test-`, `Resolve-`, `Ensure-`, `Require-`, and `Write-` consistently.
- Reserve `Ensure-` for idempotent mutation that may write files.
- Reserve `Require-` for validation that fails if data is missing.
- Avoid names that imply hidden git commits or broad repository-wide behavior.

The helper surface should feel like a convenience layer, not a mini framework.

## Open Questions

- Should helper functions be available automatically, or should conventions explicitly opt in?
- Should helpers receive the current convention name so error messages can identify the failing convention?
- Should text helpers preserve existing file encoding when possible, or always normalize to UTF-8?
- Should any helper be allowed to stage files, or should helpers remain strictly file-system focused?
- Should the CLI provide test coverage for helper semantics through executable convention integration tests?

## Initial Implementation Slice

If this idea moves forward, the smallest useful implementation would be:

1. Introduce a wrapper-based execution path for `convention.ps1`.
2. Provide cached access to parsed settings through `Get-ConventionSettings` and `Require-ConventionSetting`.
3. Add `Resolve-RepositoryPath` and `Set-TextFileIfChanged`.
4. Add a few integration tests that prove helper functions are available inside executable conventions.
5. Document the helper surface in the README once the API is stable.

That slice would remove the most common setup code without committing the project to a large PowerShell API too early.
