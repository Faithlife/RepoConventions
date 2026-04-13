# RepoConventions Tech Spec

This document captures the current implementation target for the CLI.

## Scope

- The CLI operates on the current directory only.
- The conventions configuration file is `.github/conventions.yml`.
- Support local and remote convention paths.
- Support composite conventions, executable conventions, or both in the same convention directory.

## CLI Surface

- With no arguments, show usage.
- `--commit` applies conventions and creates commits as needed.
- `--open-pr` implies `--commit`, then opens or updates a pull request.
- `--help` and `--version` should be supported.
- Do not add `--verbose` or `--dry-run` yet.

## Convention Resolution

- Resolve conventions in declaration order.
- A remote convention path uses the `owner/repo/path@ref` form.
- A local convention path starts with `./` or `../` and is resolved relative to the containing configuration file.
- Fetch remote conventions with `git clone`.
- Nested composite settings syntax should follow the GitHub Actions style eventually, but for now composite convention settings are ignored.
- If both `convention.yml` and `convention.ps1` exist, apply the composite convention first, then run the script.
- If a cycle is detected, skip the convention entry that would create the cycle and continue.

## Executable Conventions

- Run `convention.ps1` with `pwsh`.
- Run the script from the root of the target Git repository.
- Pass one argument: the path to a JSON file.
- The JSON payload contains only `settings`.
- Convention script output should flow directly to the caller.
- Under GitHub Actions, each convention run should be wrapped in its own log group.

## Git Behavior

- `--commit` and `--open-pr` create commits after each convention that leaves changes behind.
- Auto-created commit messages use `Apply convention <name>.`
- `<name>` is the convention directory name, or the repository name when the convention is at the repository root.
- Do not introduce specialized exit codes yet.

## Pull Requests

- Use `gh` to create pull requests.
- Assume `git` and `gh` are already authenticated correctly.
- If an open PR already exists for the convention branch, apply the conventions to that PR's code instead of creating a new PR.

## Remaining Open Questions

- Rollback after failure: if a later convention fails after earlier conventions already committed changes, should the CLI leave earlier successful commits in place, reset them automatically, or stop and require manual cleanup?

It should automatically clean up after the failed convention without reverting commits from previous conventions.

- PR branch strategy: should `--open-pr` use one reusable branch for all convention updates, one branch per convention, or one branch per run? What should the exact branch name be?

One branch named `repo-conventions`.

- PR metadata: what should the default PR title and body be?

Restate the question with a proposal.

- Existing PR behavior: if a matching branch exists but the PR is closed, should the CLI reopen it, create a new PR from the same branch, or create a fresh branch?

Restate the question with a proposal.

- Console output outside script passthrough: should the CLI print a short summary per convention, a final summary only, or both?

A short summary per convention

- Machine-readable output: do we want a structured output mode later, or should plain console output remain the only mode?

Restate the question with an explanation as to why we would want structured output mode later.

## Future Ideas

- Add an explicit repository path argument.
- Add config path override support.
- Validate conventions without applying them.
- Support caching for remote convention fetches.
- Support richer settings propagation for composite conventions.
- Add more structured failure and diagnostics reporting.
