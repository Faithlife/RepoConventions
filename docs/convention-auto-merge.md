# Convention Auto-Merge

## Status

Draft.

## Purpose

Applying conventions already supports creating or updating a pull request. Some repositories will want the CLI to go one step further and enable auto-merge for that pull request when the repository policy allows it.

This document proposes a first version of convention-controlled auto-merge behavior, including:

- CLI options for forcing or preventing auto-merge for a single run.
- Convention-level defaults that differ for composite and executable conventions.
- Repository-level defaults for organizations that want conventions PRs to auto-merge unless explicitly disabled.
- A way to choose the merge method used when auto-merge is enabled.

## Goals

- Allow repository operators to opt into auto-merging convention PRs.
- Allow callers to override auto-merge behavior for a specific run.
- Support GitHub's available auto-merge methods: merge, squash, and rebase.
- Keep the behavior predictable when multiple conventions are applied in one run.
- Avoid enabling auto-merge in cases where the CLI cannot prove the PR is safe to submit.

## Non-Goals

- Bypassing repository branch protection or required checks.
- Auto-approving pull requests.
- Inventing merge strategies beyond the strategies GitHub supports.
- Per-file or per-change heuristics for deciding whether a convention is safe.
- Supporting different auto-merge behavior for different conventions within the same generated PR.

## Background

Today, `apply --open-pr` creates or updates a conventions PR. The user still decides whether and how to merge that PR.

The missing capability is intent. Some convention changes are effectively maintenance automation and should merge on green by default. Other convention changes should always remain manual. The CLI needs a clear precedence model so that repository-wide policy, convention author intent, and explicit CLI overrides can coexist.

## Proposed CLI Surface

Extend the `apply` command with these options:

- `--open-pr`: Existing behavior. Creates or updates the conventions pull request.
- `--auto-merge`: After creating or updating the pull request, request GitHub auto-merge.
- `--no-auto-merge`: Explicitly disable auto-merge for this run, even if configuration would otherwise enable it.
- `--merge-method <merge|squash|rebase>`: Choose the merge method used when auto-merge is enabled for this run.

Rules:

- `--auto-merge` and `--no-auto-merge` are mutually exclusive.
- `--merge-method` is only valid when the effective result enables auto-merge.
- `--auto-merge` implies `--open-pr`.
- `--no-auto-merge` does not imply `--open-pr`; it only affects runs that already create or update a PR.

## Proposed Configuration Surface

### Repository Configuration

Add a top-level `pull-request` section in `.github/conventions.yml`:

```yaml
pull-request:
  auto-merge: true
  merge-method: squash

conventions:
  - path: owner/shared-conventions/dotnet-format@v1
```

Proposed semantics:

- `pull-request.auto-merge` sets the repository default for generated conventions PRs.
- `pull-request.merge-method` sets the default merge method when auto-merge is enabled.
- If omitted, the repository default is manual merge behavior.

This keeps repository-wide policy in the file that already governs convention application.

### Convention Configuration

Allow each convention reference to express its preferred PR behavior:

```yaml
conventions:
  - path: owner/shared-conventions/dotnet-format@v1
    pull-request:
      auto-merge: true
      merge-method: squash
```

Proposed semantics:

- `pull-request.auto-merge` on a convention reference acts as the default intent contributed by that convention.
- `pull-request.merge-method` expresses the convention's preferred merge method if that convention is the one determining the effective method.
- These values affect the generated PR as a whole, not just the commit produced by that convention.

This is intentionally attached to the convention reference in the consuming repository rather than the remote convention's own internal files. The repository consuming the convention remains in control of whether a given referenced convention should flow through automatically.

## Convention-Type Defaults

If a convention reference does not explicitly set `pull-request.auto-merge`, its default should depend on the convention type:

- Executable convention: default to `false` for safety.
- Composite convention: default to `true` only if all of its child conventions resolve to `true`.

For composite conventions, an explicit `pull-request.auto-merge` value on the composite convention reference overrides whatever would otherwise be inferred from its children.

This makes executable conventions conservative by default while still allowing a purely declarative composite convention to opt into auto-merge transitively.

## Effective Behavior Model

The CLI should compute one effective auto-merge decision for the entire PR.

Precedence, highest to lowest:

1. CLI override.
2. Repository-level `.github/conventions.yml` `pull-request` settings.
3. Explicit convention-level `pull-request` settings on applied convention references.
4. Convention-type defaults inferred from applied convention references.
5. Default behavior: auto-merge disabled.

Rationale:

- CLI flags should always win because they express operator intent for the current run.
- Repository settings should override convention defaults because the repository owns its own PR policy.
- Explicit convention settings should override inferred convention-type defaults.
- Convention settings are still useful as a low-friction default when a repository has not expressed a policy.

## Multiple Conventions in One PR

One run may apply multiple conventions into the same PR. That requires a deterministic reduction rule.

Proposed rules:

- If repository-level `pull-request.auto-merge` is set, it decides the effective value for the whole PR.
- Otherwise, if any applied convention explicitly sets `pull-request.auto-merge: false`, the effective value becomes false.
- Otherwise, if one or more applied conventions explicitly sets `pull-request.auto-merge: true`, the effective value becomes true.
- Otherwise, if every applied convention resolves to a convention-type default of `true`, the effective value becomes true.
- Otherwise, the effective value remains false.

This makes `false` the safer conflict resolver when repository policy is absent.

For merge method selection:

- CLI `--merge-method` wins when provided.
- Otherwise repository `pull-request.merge-method` wins when provided.
- Otherwise use the single explicit convention-level method if all conventions that specify a method agree.
- Otherwise fall back to a default method of `squash`.

If conventions disagree on merge method and repository policy does not settle the conflict, the CLI should choose `squash` and print a note explaining why.

## GitHub Integration Behavior

When the effective result enables auto-merge:

- The CLI creates or updates the PR as usual.
- The CLI requests GitHub auto-merge on that PR using the effective merge method.
- If auto-merge is already enabled with the same method, no further action is needed.
- If auto-merge is already enabled with a different method, update it to the new method if GitHub supports that transition; otherwise disable and re-enable it.

When the effective result disables auto-merge:

- The CLI should leave the PR open without auto-merge.
- `--no-auto-merge` should only avoid enabling auto-merge during the current run. It should not disable auto-merge if the PR already has it enabled.
- Repository or convention settings that resolve to disabled should likewise leave any already-enabled PR auto-merge state unchanged.

## Safety Rules

The CLI should only attempt auto-merge when all of the following are true:

- The run is already creating or updating a PR.
- The target repository supports auto-merge.
- The authenticated `gh` session has permission to enable auto-merge on the PR.
- The PR is not in a state that GitHub rejects immediately, such as an invalid base branch.

If the user asked for `--auto-merge` explicitly and the CLI cannot enable it, the command should fail.

If auto-merge is enabled only through configuration and the CLI cannot enable it, the command should warn and continue after creating or updating the PR. This keeps configuration defaults helpful without turning them into brittle failures.

## Merge Method Semantics

The merge method governs the final history that lands on the base branch.

- `merge`: preserves the PR branch topology and all convention commits.
- `squash`: produces one commit on the base branch regardless of how many convention commits were created on the PR branch.
- `rebase`: replays the convention commits onto the base branch without a merge commit.

Recommended default: `squash`.

Reasoning:

- A conventions PR may contain several commits because the CLI creates one commit per convention.
- `squash` keeps the base branch history compact while still allowing the PR itself to show the per-convention commit structure during review.
- `merge` is noisier and should be opt-in.
- `rebase` may be acceptable, but it preserves all convention commits on the base branch and can make automation history harder to scan.

## Console Output

When a PR run computes effective auto-merge behavior, print a short summary such as:

```text
Pull request auto-merge: enabled (method: squash, source: repository config)
```

Possible sources:

- `cli`
- `repository config`
- `explicit convention config`
- `inferred convention defaults`
- `default`

If conventions disagree and the CLI falls back to `squash`, print a note.

If auto-merge is requested but cannot be enabled, print the GitHub error before failing or warning.

## Remaining Question

- If the selected `merge-method` is not allowed by the repository, should the CLI detect allowed methods up front, or should it attempt the requested method and rely on the GitHub failure response?

Current leaning:

- Detect allowed methods when GitHub exposes that capability cheaply.
- Do not silently fall back to a different method after a failure, because that would make the final merge shape less predictable than the requested configuration.

## Suggested Implementation Order

1. Add repository-level `pull-request.auto-merge` and `pull-request.merge-method` support.
2. Add CLI overrides and precedence handling.
3. Add GitHub auto-merge enable and disable operations.
4. Add convention-type defaults and reduction rules for executable and composite conventions.
5. Add integration tests covering explicit enable, explicit disable, inherited defaults, and merge-method conflict handling.
