# CLI Path Overrides

## Status

Implemented.

## Purpose

`repo-conventions` currently assumes three location defaults:

- the target repository is the current working directory
- the configuration file is `.github/conventions.yml` under that repository root
- temporary files and remote clones live under the system temp directory

This document proposes explicit CLI overrides for each location so callers can operate on another repository, use a non-standard configuration file, or redirect temporary artifacts to a caller-controlled directory.

## Goals

- Allow the CLI to target a repository other than the current working directory.
- Allow the CLI to read or update a configuration file other than `.github/conventions.yml`.
- Allow the CLI to place remote clone directories and transient JSON input files under a caller-selected temp root.
- Preserve current behavior when no override is supplied.
- Keep path normalization and validation explicit so error messages stay understandable.

## Non-Goals

- Auto-discovering a repository root from an arbitrary subdirectory without an explicit override.
- Changing how convention reference paths are interpreted inside configuration files.
- Allowing different temp roots for different internal operations.
- Replacing the existing repository-root cleanliness requirements for `apply`.

## Current Behavior

Today `Program.cs` passes `Environment.CurrentDirectory` into `RepoConventionsCli.InvokeAsync`, and that value is treated as the target repository root for both `add` and `apply`.

`RepoConventionsCli` hardcodes the top-level configuration path to `.github/conventions.yml` under that working directory.

Temp handling is split across two call sites:

- `TemporaryDirectoryPath.Create()` chooses a unique directory under `Path.GetTempPath()` for remote convention clones.
- `ConventionRunner.RunConventionScriptAsync` uses `Path.GetTempFileName()` for the JSON file passed to `convention.ps1`.

That split means a temp override needs a shared abstraction, not just a change to `TemporaryDirectoryPath`.

## Proposed CLI Surface

Add root-level options that apply to both `add` and `apply`.

- `--repo <path>`: target repository root. Defaults to the current process directory.
- `--config <path>`: top-level conventions file. Defaults to `.github/conventions.yml` relative to the target repository root.
- `--temp <path>`: temp root used for RepoConventions-owned transient files and directories. Defaults to the system temp directory.

Examples:

```pwsh
repo-conventions apply --repo ../target-repo
repo-conventions apply --repo ../target-repo --config .github/custom-conventions.yml
repo-conventions apply --repo ../target-repo --temp .artifacts/repo-conventions-temp
repo-conventions add ./conventions/my-convention --repo ../target-repo --config .config/repo-conventions.yml
```

## Path Resolution Rules

`--repo`

- If relative, resolve against the current process directory.
- Normalize to a full path before validation.

`--config`

- If omitted, resolve to `<repository>/.github/conventions.yml`.
- If relative, resolve against the resolved repository root.
- If absolute, use as-is.

`--temp`

- If omitted, use `Path.GetTempPath()` as the temp root.
- If relative, resolve against the resolved repository root.
- If absolute, use as-is.
- Treat the value as a parent directory controlled by RepoConventions, not as a single fixed temp file path.

## Validation Rules

Repository validation:

- Continue requiring the resolved repository path to be the git repository root for both commands.
- Continue requiring `apply` to start from a clean repository and `apply --open-pr` to have no unpushed commits.

Config validation:

- `apply` should fail if the resolved config file does not exist.
- `add` should continue creating the resolved config file when it is missing.
- Error and success messages should mention the actual resolved config path or a user-meaningful display form rather than always hardcoding `.github/conventions.yml`.

Temp validation:

- Create the temp root on demand if it does not already exist.
- Fail with a clear error if the configured temp root cannot be created or written.

## Implementation Plan

1. Add root command options for `--repo`, `--config`, and `--temp`, and pass them into both command handlers.
2. Introduce a small path-resolution object, such as `CliPathSettings` or `ResolvedCliPaths`, so resolution happens once and downstream code receives normalized paths.
3. Update `ExecuteApplyAsync` and `ExecuteAddAsync` to use the resolved repository root and resolved config path instead of recomputing `.github/conventions.yml`.
4. Extend `ConventionRunnerSettings` with the resolved temp root and thread it into `ConventionRunner`.
5. Replace the current temp helpers with a single abstraction that can create both unique directories and unique files beneath the configured temp root.
6. Update `RunConventionScriptAsync` to allocate its JSON input file through that abstraction instead of `Path.GetTempFileName()`.
7. Update any status and error messages that currently assume the default config path so they stay accurate under overrides.
8. Keep the public `Program` entry point defaulting to `Environment.CurrentDirectory` when `--repository` is not supplied.

## Suggested Internal Shape

One workable structure is:

- `ResolvedCliPaths.RepositoryRoot`
- `ResolvedCliPaths.ConfigurationPath`
- `ResolvedCliPaths.TempRoot`

And one temp helper such as:

- `TemporaryPathFactory.CreateDirectory(tempRoot)`
- `TemporaryPathFactory.CreateFile(tempRoot, extension)`

This keeps path-policy decisions out of `ConventionRunner` and avoids sprinkling `Path.GetTempPath()` and `Path.GetTempFileName()` through the codebase.

## Test Plan

- Add `AddCommandTests` coverage for `--repo` so the command can be invoked from outside the target repo while still updating the target config file.
- Add `AddCommandTests` coverage for `--config` so a non-default config file is created and updated.
- Add `ConventionExecutionTests` coverage for `apply --config` so conventions are loaded from a custom config path.
- Add `ConventionExecutionTests` coverage for `apply --repo` invoked from another directory.
- Add `OpenPrTests` coverage to verify `--repo` and `--config` still participate correctly in PR creation.
- Add targeted tests for `--temp` that verify both remote clone directories and script input files are created under the configured temp root.
- Add failure-path tests for a missing custom config on `apply` and for an unwritable temp root.

## Documentation Changes

- Update `README.md` CLI examples to show the new options.
- Update `docs/configuration.md` or command documentation to clarify that config location is now a CLI concern, not a YAML concern.
- Document the resolution rules for relative `--repo`, `--config`, and `--temp` values.

## Decisions

- `--config` is accepted on both `add` and `apply`.
- `--temp` is exposed globally, even though the current implementation only consumes it during `apply`.
- Messages prefer relative display paths when possible to reduce noise.
