# Authoring Published Conventions

This document covers how to create and maintain convention directories that other repositories consume through RepoConventions.

## Convention Directory Layout

A convention directory may contain either of these files, or both:

- `convention.yml` for composition
- `convention.ps1` for executable behavior

If both files are present, RepoConventions applies `convention.yml` first and then runs `convention.ps1`.

Convention directories can also include supporting files such as `README.md`, templates, or text files consumed by settings expressions.

## Composite Conventions

Use `convention.yml` to compose other conventions.

Example:

```yaml
conventions:
  - path: ../dotnet-sdk
    settings:
      version: 10
  - path: ../dotnet-slnx
```

Composite conventions can propagate parent settings into child settings.

Example:

```yaml
conventions:
  - path: ../dotnet-sdk
    settings:
      version: ${{ settings.sdk.version }}
```

Supported behaviors:

- Child conventions are applied in declaration order.
- Relative child paths are resolved relative to the YAML file that contains them.
- Root-relative paths beginning with `/` resolve from the target repository root.

## Settings Expressions

Composite conventions support two expression forms inside child settings.

`settings` lookup:

```yaml
settings:
  version: ${{ settings.sdk.version }}
```

- Reads a value from the parent convention's settings object.
- Preserves JSON-compatible value types such as strings, numbers, booleans, arrays, objects, and null.

`readText("path")`:

```yaml
settings:
  body: ${{ readText("./body.txt") }}
  message: prefix-${{ readText("/docs/name.txt") }}-suffix
```

- Reads UTF-8 text from a file.
- Relative paths resolve from the YAML file that contains the expression.
- Paths beginning with `/` resolve from the target repository root.
- Native absolute filesystem paths are not allowed.
- Paths must stay within the target repository.

## Executable Conventions

Use `convention.ps1` when the convention needs to inspect repository state or rewrite files.

Behavior:

- The script runs with `pwsh`.
- The current working directory is the target repository root.
- The first argument is the path to a JSON file that contains a `settings` property.

Minimal pattern:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$conventionInput = Get-Content -Raw $args[0] | ConvertFrom-Json
$settings = $conventionInput.settings
```

Authoring expectations:

- Make the script idempotent.
- Return exit code `0` when the repo is already compliant or after fixing it.
- Use a non-zero exit code only when the convention cannot complete.
- Avoid interactive prompts and machine-local dependencies.
- Prefer deterministic output and stable file ordering.

## Commit Behavior

- If an executable convention leaves tracked or untracked changes behind and does not create its own commit, RepoConventions creates a commit automatically.
- If a convention creates its own commits, those commits are preserved.
- If a convention fails, RepoConventions resets the target repository back to its pre-convention HEAD.

## Documentation Expectations

Each published convention directory should include a `README.md` that documents:

- what the convention does
- which settings it accepts
- any tool or framework assumptions
- whether it is intended to be composed or consumed directly

## Testing Guidance

Test against a temporary Git repository and verify:

- the already-compliant case
- the non-compliant case
- a second successful run produces no changes
- any supported non-default settings behave correctly

If you use Pester, keep syntax compatible with Pester 3.x.
