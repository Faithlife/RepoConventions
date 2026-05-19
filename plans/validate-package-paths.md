# Validate Package Paths Plan

## Goal

Add path validation in two places:

- `repo-conventions add <path>` validates every new package path before mutating the configuration file.
- `repo-conventions validate` validates all package paths already present in the conventions configuration file without applying convention scripts or changing the working tree.

In the current codebase these are convention paths, but this plan uses package path where that matches the feature request.

## Current Behavior

- `add` writes each supplied path directly through `ConventionRunner.AddAsync` and only detects missing or malformed conventions later if `--apply` or `--open-pr` is used.
- `apply` already builds the complete convention plan before running scripts. During that planning step it resolves local and remote paths, enforces local path containment, verifies each convention directory exists, requires `convention.yml` or `convention.ps1`, parses child `convention.yml` files, and detects cycles.
- The plan-building behavior is currently private to `ConventionRunner`, so `add` and a new command cannot ask for validation without also entering the apply flow.

## Proposed CLI Behavior

### `add`

- Before changing `.github/conventions.yml` or a custom `--config` file, validate each path supplied on the command line.
- Resolve paths exactly as they will be resolved after insertion:
  - `./` and `../` paths are relative to the directory containing the conventions configuration file.
  - `/` paths are relative to the target repository root.
  - remote paths use the existing `owner/repo/path@ref` parser and clone behavior.
- If any supplied path is invalid, return exit code `1`, write the validation errors to stderr, and leave the configuration file unchanged.
- If all supplied paths are valid, keep the existing add behavior for creating or appending the configuration file, duplicate detection, `--commit`, `--apply`, and `--open-pr`.

### `validate`

- Add a new command:

```pwsh
repo-conventions validate [options]
```

- Support the same path options as `apply` and `add`: `--repo`, `--config`, and `--temp`.
- Require the target repository path to be a Git repository root, matching `apply` and `add`.
- Require the configuration file to exist. If it is missing, fail with the same style of message used by `apply`.
- Load the top-level configuration and validate the complete convention graph without running any `convention.ps1` scripts, creating commits, checking repository cleanliness, opening pull requests, or mutating files.
- Return `0` when all paths are valid. Return `1` when configuration loading or path validation fails.
- Print a concise success message, for example `Validated 3 conventions.` The count should be the number of planned convention occurrences, matching the plan produced by `apply`.

## Validation Rules

Use the same rules that `apply` already depends on during plan construction:

- Configuration YAML is valid and has a `conventions` sequence where required.
- Every convention reference has a non-empty `path`.
- Local paths resolve inside the repository that contains the YAML file that declared the reference.
- Native absolute local paths are rejected by falling through to remote-path parsing and failing as invalid, or explicitly rejected with a clearer message if the resolver is factored out.
- The resolved convention directory exists.
- The resolved convention directory contains `convention.yml`, `convention.ps1`, or both.
- If `convention.yml` exists, it parses successfully.
- A script-only convention can omit `convention.yml`; a composite-only convention can omit `convention.ps1` if its `convention.yml` has conventions.
- Child convention paths are validated recursively using the child configuration file's containing directory and repository root.
- Cycles are not errors; they should be reported the same way as apply planning currently reports them and skipped during recursion.
- Remote convention paths clone and optionally checkout the requested ref. Clone or checkout failures are validation failures.

## Implementation Steps

- Introduce a validation entry point on `ConventionRunner`, for example `ValidateAsync(string topLevelConfigPath, CancellationToken cancellationToken)`.
- Add a second validation entry point for prospective paths before insertion, for example `ValidateConventionPathsAsync(string configurationPath, IReadOnlyList<string> conventionPaths, CancellationToken cancellationToken)`.
- Reuse the existing `BuildConventionPlanAsync` path so validation and apply cannot drift. The implementation should build a `ConventionFileConfiguration` from the supplied paths for pre-add validation instead of writing a temporary YAML file.
- Consider extracting the private resolution and plan-building result into small internal types if needed, but keep behavior inside `ConventionRunner` unless reuse becomes awkward.
- Add `ExecuteValidateAsync` to `RepoConventionsCli` with the same repository-root and missing-config checks used by `ExecuteApplyAsync`, excluding the clean-repository check.
- Register the `validate` command in `RepoConventionsCli.InvokeAsync` next to `apply` and `add`.
- In `ExecuteAddAsync`, construct `ConventionRunner` before calling `AddAsync`, validate the command-line paths, and only then mutate the configuration file.
- Preserve cancellation handling and `ProgramException` formatting patterns used by the existing commands.
- Update README CLI reference and examples to document `validate` and the new `add` validation behavior.

## Test Plan

- Add `ValidateCommandTests` for:
  - missing config fails with the existing missing configuration message style
  - invalid YAML fails with a friendly YAML message
  - valid local script convention succeeds and reports the count
  - valid composite convention with child paths succeeds
  - missing local convention directory fails
  - local child path escaping the containing repository root fails
  - convention directory with neither `convention.yml` nor `convention.ps1` fails
  - remote convention path succeeds using the existing fake remote repository pattern
  - remote checkout failure reports the bad ref
  - validate succeeds when the working tree is dirty
- Update existing `AddCommandTests` that currently add missing paths without creating convention directories. They should either create a minimal convention before adding or assert the new failure behavior when the path is missing.
- Add new `AddCommandTests` for:
  - missing supplied path fails and does not create `.github/conventions.yml`
  - one invalid path among multiple paths fails and leaves an existing config unchanged
  - valid local path is added successfully
  - valid remote path is added successfully using the fake remote repository resolver
  - `--commit`, `--apply`, and `--open-pr` still happen only after validation succeeds
- Run focused tests first with the add and validate test fixtures, then run `./build.ps1 test` before merging.

## Open Questions

- Should `add` validate paths that are already present in the configuration, or only new paths it would append? The stricter and simpler behavior is to validate every supplied path before deciding whether it is already present.
- Should unpinned remote paths produce warnings? The README already warns about this risk, but making it part of validation could be a separate policy decision.
- Should `validate` emit one line per convention path or only a summary? A summary is less noisy; detailed success output can be added later if users ask for diagnostics.
