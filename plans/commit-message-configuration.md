# Plan: Convention Commit Message Configuration

## Goal

Allow a convention to provide the commit message RepoConventions uses when the convention script leaves uncommitted changes behind. The message can be configured in the convention's own `convention.yml` or on a reference to another convention. A reference-level message overrides the convention definition message.

## Configuration Shape

Support a new optional `commit` object with a `message` property on convention definitions and convention references:

```yaml
# convention.yml inside a convention directory
commit:
  message: Update repository build configuration
```

```yaml
# .github/conventions.yml or a parent convention.yml
conventions:
  - path: ./conventions/dotnet-sdk
    commit:
      message: Update .NET SDK configuration
```

The effective commit message for a planned executable convention should be resolved in this order:

- Reference-level `commit.message`.
- Convention definition `commit.message`.
- Existing default: `Apply convention {displayName}`.

Do not add a top-level repository `commit` setting. This is per convention, unlike repository-wide pull request settings.

## Implementation Steps

- Add a `CommitSettings` record with `string? Message`.
- Add `CommitSettings? Commit` to `ConventionFileConfiguration` and `ConventionReference`.
- Extend `ConventionConfiguration` so both `ConfigurationFile` and `ConventionRecord` deserialize an optional `commit` object, with a nested `CommitRecord` containing `[JsonPropertyName("message")] string? Message`.
- Normalize `commit.message` when converting records:
  - `null`, empty, or whitespace-only strings mean unspecified.
  - non-empty strings are accepted and preserved.
- Add a `MergeCommitSettings` helper near `MergePullRequestSettings`; reference settings override definition settings, with no list-merging behavior.
- Add `CommitSettings? Commit` to `PlannedConvention`.
- During planning, load `conventionConfiguration.Commit` from the convention's own `convention.yml`, merge it with `reference.Commit`, and store the effective settings on the planned executable convention.
- Update `RunConventionScriptAsync` or its call site so the auto-commit message comes from the effective commit settings. Keep the existing default message for conventions without `commit.message`.
- Keep the behavior limited to the automatic commit created for leftover script changes. If a convention script creates its own commits, do not rewrite those messages.

## Tests

- Add configuration parsing tests or execution tests covering `commit.message` on a convention reference.
- Add an execution test covering `commit.message` in the convention's own `convention.yml`.
- Add an execution test proving reference-level `commit.message` overrides the convention definition message.
- Add a regression test proving the default message remains `Apply convention {displayName}` when no commit message is configured.
- Add coverage proving empty or whitespace-only `commit.message` values are treated as unspecified and fall back to the default message.
- Add or update README assertions indirectly through tests only if this repo already tests README examples; otherwise update docs manually.

## Documentation

- Update `README.md` configuration examples to show `commit.message` at the reference level.
- Add a short `Commit Settings` section near `Pull Request Settings` explaining that `commit.message` can appear in a convention's `convention.yml` or on a reference, and that reference-level settings override convention defaults.

## Verification

- Run focused NUnit tests for convention execution and configuration parsing after implementation.
- Run `dotnet format` on touched C# files if analyzer or style issues appear.
- Before the final implementation commit, run `./build.ps1 test` as required by the repo guidelines.