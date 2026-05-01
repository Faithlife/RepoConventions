# RepoConventions Configuration

This document covers how to configure a repository that consumes conventions.

## Configuration File

RepoConventions reads `.github/conventions.yml` from the repository root by default. Use `--config` to point at a different conventions file.

Example:

```yaml
pull-request:
  labels:
    - automation
  draft: true
  reviewers:
    - octocat
  assignees:
    - octocat
  auto-merge: false

conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk@v1
    settings:
      version: 10
  - path: ./conventions/local-policy
    pull-request:
      labels:
        - dependencies
```

## Top-Level Properties

`conventions`

- Required.
- Applied in declaration order.
- Each entry must contain `path`.
- Each entry may contain `settings` and `pull-request`.

`pull-request`

- Optional.
- Used only when running `repo-conventions apply --open-pr`.
- Supports `labels`, `reviewers`, `assignees`, `draft`, `auto-merge`, and `merge-method`.

## Convention Paths

Use one of these path forms.

`owner/repo/path@ref`

- Resolves from a GitHub repository.
- `path` is optional; omitting it targets the repository root.
- `@ref` is optional; omitting it uses the remote repository's default branch.

`./relative/path` or `../relative/path`

- Resolves relative to the YAML file that contains the reference.
- Useful for conventions stored in the same repository.

`/root/relative/path`

- Resolves from the target repository root.
- Useful when the configuration file is nested indirectly through composite conventions but the referenced files live at a stable repo-root location.

## Settings

`settings` is passed through to the target convention as JSON-compatible data.

Example:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk
    settings:
      version: 10
      preview: false
      feeds:
        - https://api.nuget.org/v3/index.json
```

Use plain YAML scalars, arrays, and objects. The convention decides what settings it supports.

## Pull Request Settings

Both the repository-level config and individual convention entries can contribute PR metadata.

Supported properties:

- `labels`
- `reviewers`
- `assignees`
- `draft`
- `auto-merge`
- `merge-method`

Example:

```yaml
pull-request:
  labels:
    - automation
  draft: true
  auto-merge: true
  merge-method: squash

conventions:
  - path: ./conventions/update-dependencies
    pull-request:
      labels:
        - dependencies
      reviewers:
        - my-org/dotnet-team
```

Notes:

- Convention-level PR settings are only relevant when that convention actually contributes commits.
- An executable convention stored in the target repository may also declare its own `pull-request` settings in `convention.yml` without declaring child `conventions`.
- RepoConventions always applies its own `repo-conventions` label to generated PRs, even though it omits that label from the status summary.
- When `draft` is enabled, new pull requests are created as drafts. Existing PRs keep their current draft or ready state.
- `repo-conventions apply --open-pr --draft` and `repo-conventions apply --open-pr --no-draft` override configured draft behavior for a single run.
- When auto-merge is enabled, reviewers and assignees are not requested on PR creation.
- CLI flags can override configured auto-merge behavior for a single run.

## CLI Path Overrides

These options affect how the CLI locates the target repository and conventions file:

- `--repo <path>` resolves the target repository root relative to the current process directory.
- `--config <path>` resolves relative to the target repository root and defaults to `.github/conventions.yml`.
- `--temp <path>` resolves relative to the target repository root and defaults to the system temp directory.

RepoConventions uses relative path forms in status and error messages when that keeps output shorter.

## Commands

Add a reference:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/dotnet-sdk@v1
repo-conventions add ./conventions/local-policy
repo-conventions add ./conventions/dotnet-sdk ./conventions/github-actions
repo-conventions add ./conventions/local-policy --repo ../target-repo --config .config/repo-conventions.yml
repo-conventions add ./conventions/local-policy --open-pr
```

Apply conventions:

```pwsh
repo-conventions apply
repo-conventions apply --git-no-verify
repo-conventions apply --repo ../target-repo --config .config/repo-conventions.yml --temp .artifacts/repo-conventions-temp
```

Apply conventions and open a PR:

```pwsh
repo-conventions apply --open-pr
repo-conventions apply --open-pr --git-no-verify
repo-conventions apply --open-pr --draft
repo-conventions apply --open-pr --no-draft
repo-conventions apply --open-pr --auto-merge --merge-method squash
repo-conventions apply --open-pr --repo ../target-repo --config .config/repo-conventions.yml
repo-conventions add ./conventions/local-policy --open-pr --git-no-verify
```

## Operational Requirements

- Run from the Git repository root.
- `apply` requires a clean working tree.
- `add --open-pr` and `apply --open-pr` require a clean working tree, a non-detached branch, and no unpushed commits.
- PR automation expects the GitHub CLI to be available and authenticated.

## Related Docs

- See [../README.md](../README.md) for the quick start.
- See [authoring-conventions.md](authoring-conventions.md) for writing conventions.
