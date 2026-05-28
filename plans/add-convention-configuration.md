# Plan: Add Convention Configuration During `repo-conventions add`

## Goal

Allow `repo-conventions add` to add a convention reference together with reference-level configuration, so users do not need to hand-edit `.github/conventions.yml` after adding a configurable convention.

Example target output:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dependency-updates
    pull-request:
      auto-merge: true
```

## Recommended CLI Shape

Do not use `--config` for the new option. `--config` already means "path to the conventions configuration file" across `add`, `apply`, and `validate`, and changing it would be ambiguous and breaking.

Add a new `add`-only option:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/dependency-updates --convention-config "{pull-request: {auto-merge: true}}"
```

The option value is a YAML mapping fragment for one convention reference. Supported top-level keys should match the existing convention reference shape:

- `settings`
- `pull-request`
- `commit`

Disallow `path` in the fragment because the command already gets the path from the positional argument.

For the first implementation, require exactly one convention path when `--convention-config` is provided. This keeps the behavior clear and avoids accidentally applying one configuration blob to multiple convention references.

## Behavior

- If the target configuration file is missing, create it with the configured convention reference.
- If the target configuration file exists, append the configured reference while preserving comments, surrounding YAML, and line endings as much as the existing `add` implementation does.
- If the convention path is already present and `--convention-config` is provided, fail with a friendly message instead of silently replacing or merging existing configuration.
- Validate the convention path before modifying the file, as `add` does today.
- Validate the YAML fragment before modifying the file.
- Reparse the updated configuration after patching and fail with the existing text-patch style diagnostic if the generated YAML is invalid.
- `--commit`, `--apply`, and `--open-pr` should continue to operate on the updated configuration after the reference is added.

## Parsing And Data Model

- Add a small internal representation for the fragment, for example `ConventionReferenceConfiguration`, containing `JsonNode? Settings`, `PullRequestSettings? PullRequest`, and `CommitSettings? Commit`.
- Parse the option value using the same YAML-to-JSON approach already used by `ConventionConfiguration`, not ad hoc string parsing.
- Reject YAML fragments that are not mappings.
- Reject unknown top-level keys so typos such as `pullrequest` do not get written and ignored later.
- Reuse the existing conversion and validation rules for `pull-request` and `commit`, including treating whitespace-only commit messages as unspecified.

## Implementation Steps

- In `RepoConventionsCli`, add an `Option<string>` named `--convention-config` only to the `add` command.
- Extend `AddCommandSettings` to carry the parsed convention reference configuration, or add a separate parameter to `ConventionRunner.AddAsync` if that reads cleaner.
- In `TryGetAddCommandSettings`, enforce that `--convention-config` is absent or parseable; keep add/apply/open-pr option validation unchanged.
- After parsing command arguments, enforce that `--convention-config` is only used with one convention path.
- In `ConventionConfiguration`, add an overload such as `AddConventionReference(string configurationPath, ConventionReference reference)` and keep `AddConventionPath` as a compatibility wrapper.
- Update missing-file creation to serialize the complete reference, not only the path.
- Update existing-file insertion so the inserted YAML block can include `settings`, `pull-request`, and `commit` children under the new `- path:` line.
- Keep the insertion logic event-based for locating the append point, then serialize only the new reference block to YAML for the inserted text.
- Ensure the generated block is indented relative to `insertionPlan.ItemIndentation` and uses the target file's newline sequence.

## Tests

Add focused tests in `AddCommandTests`:

- Creates a missing `.github/conventions.yml` with `--convention-config "{settings: {file: docs/example.md, overwrite: false}}"`.
- Appends a configured reference to an existing file without dropping existing settings, comments, or trailing content.
- Supports reference-level pull request configuration with the user's example shape: `{pull-request: {auto-merge: true}}`.
- Supports reference-level commit configuration: `{commit: {message: Refresh generated files}}`.
- Fails without changing the file when the YAML fragment is invalid.
- Fails without changing the file when the fragment contains unknown top-level keys or `path`.
- Fails with a friendly message when `--convention-config` is used with multiple convention paths.
- Fails with a friendly message when the path is already present and configuration was provided.

Add or update help/usage assertions only if this repo already has CLI help tests; otherwise keep the coverage at behavior level.

## Documentation

- Update `README.md` near the existing `repo-conventions add` example with a short configured-add example.
- Update `skills/repo-conventions/references/repository-configuration.md` to mention that configured references can be created by `repo-conventions add --convention-config`.
- Keep the existing `--config` documentation as-is for the configuration file path.

## Future Options

- Add `--settings <yaml>` as shorthand for `--convention-config "{settings: ...}"` if users commonly pass only convention settings.
- Add repeated path/config pairs later if multi-add with distinct configuration becomes important.
- Consider a future major-version rename of the existing file-path `--config` option to `--config-file`, but do not couple that breaking change to this feature.
