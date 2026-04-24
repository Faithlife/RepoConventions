# Pull Request Draft Setting

## Status

Implemented.

## Purpose

`repo-conventions` already supports pull request metadata and merge behavior through the `pull-request` configuration surface.

This document proposes adding draft PR creation as another pull request setting so repositories can keep generated PRs out of the ready-for-review queue until they are explicitly promoted.

## Goals

- Allow repositories to declare that generated conventions PRs should be created as drafts.
- Allow individual convention references to contribute draft intent in the same place they already contribute labels, reviewers, assignees, auto-merge, and merge-method.
- Keep the implementation aligned with the existing `PullRequestSettings` and `PullRequestBehavior` flow.
- Limit the first version to creation-time behavior that maps directly to GitHub CLI support.

## Non-Goals

- Converting an existing open conventions PR from ready to draft.
- Converting an existing draft conventions PR to ready for review.
- Reworking the existing auto-merge, reviewer, assignee, or label rules.

## Current Behavior

Today the effective pull request behavior is computed in `ConventionRunner.BuildPullRequestBehavior`, but the resulting `PullRequestBehavior` only carries labels, reviewers, assignees, auto-merge, and merge-method.

New pull requests are created through `gh pr create` in `BuildCreatePullRequestArguments`. There is no draft flag in that argument builder, so all created PRs are opened as ready for review.

## Proposed Configuration Surface

Add a nullable `draft` property to the existing `pull-request` configuration object.

Repository-level example:

```yaml
pull-request:
  draft: true

conventions:
  - path: owner/shared-conventions/dotnet-format@v1
```

Convention-level example:

```yaml
conventions:
  - path: owner/shared-conventions/dotnet-format@v1
    pull-request:
      draft: true
```

## Effective Draft Model

The effective draft decision should be computed once for the generated PR.

Precedence, highest to lowest:

1. CLI override: `--draft` or `--no-draft`.
2. Repository-level `pull-request.draft` when explicitly set.
3. Contributing convention-level `pull-request.draft` values.
4. Default behavior: `false`.

Reduction rule for contributing conventions when the repository-level setting is absent:

- If any contributing convention sets `draft: true`, create the PR as a draft.
- Otherwise, create the PR as ready for review.

Rationale:

- Draft is a safety-oriented setting.
- A request to keep the PR out of normal review flow should win over silence.
- Unlike auto-merge, there is no separate executable-convention heuristic needed here.

## Create And Update Semantics

CLI override path:

- `--draft` forces the effective result to draft.
- `--no-draft` forces the effective result to ready for review.
- `--draft` and `--no-draft` are mutually exclusive.

Creation path:

- When the effective result is draft, pass `--draft` to `gh pr create`.
- When the effective result is not draft, keep the current ready-for-review behavior.

Update path:

- Do not change the state of an existing PR in the first version.
- Preserve whatever state the PR is already in.
- Continue updating body, labels, reviewers, assignees, and auto-merge exactly as today.

Rationale:

- `gh pr create --draft` maps cleanly onto current behavior.
- Existing PR state transitions would require additional commands or GraphQL mutations and a policy decision about whether automation is allowed to flip reviewer-visible state after creation.

## Implementation Plan

1. Extend `PullRequestSettings` with `bool? Draft` and update YAML serialization/deserialization coverage accordingly.
2. Extend `PullRequestBehavior` with a `bool Draft` field and compute it inside `BuildPullRequestBehavior` using the precedence rules above.
3. Update `BuildCreatePullRequestArguments` to append `--draft` when the effective behavior requires it.
4. Keep `CompleteExistingPullRequestAsync` unchanged with respect to draft status.
5. Omit `draft` from PR announcement details in the first version to avoid implying that updates changed PR state.
6. Update docs so `draft` appears anywhere the supported `pull-request` properties are enumerated.

## Test Plan

- Add an `OpenPrTests` case that verifies repository-level `pull-request.draft: true` adds `--draft` to `gh pr create`.
- Add an `OpenPrTests` case that verifies a contributing convention with `pull-request.draft: true` also adds `--draft`.
- Add an `OpenPrTests` case that verifies non-contributing conventions do not affect draft behavior.
- Add an `OpenPrTests` case that verifies `--draft` overrides configuration.
- Add an `OpenPrTests` case that verifies `--no-draft` overrides configuration.
- Add an `OpenPrTests` case that verifies `--draft` and `--no-draft` are rejected together.
- Add an `OpenPrTests` case that verifies an existing open PR update path does not attempt any draft-state mutation.
- Update docs tests or snapshots if the repository later adds them for supported configuration properties.

## Documentation Changes

- Update `README.md` to mention `draft` in the list of supported PR settings.
- Update `docs/configuration.md` to document `pull-request.draft` at both repository and convention scope.
- Optionally cross-link from `plans/convention-pull-requests.md` if we want the broader PR behavior plan to reference this narrower follow-up.

## Open Questions

- If we later support changing existing PR draft state, should repository-level `draft: false` force ready-for-review, or should existing human-chosen state win after creation?
