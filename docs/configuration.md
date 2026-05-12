# RepoConventions Configuration

This document covers how to configure a repository that consumes conventions.

## Configuration File

RepoConventions reads `.github/conventions.yml` from the repository root by default. Use `--config` to point at a different conventions file.

Example:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk
    settings:
      version: 10
  - path: ./conventions/local-policy
    pull-request:
      labels:
        - dependencies

pull-request:
  labels:
    - automation
  draft: true
  reviewers:
    - octocat
  assignees:
    - octocat
  auto-merge: false
```

### Top-Level Properties

`conventions`

- Required.
- Applied in declaration order.
- Each entry must contain `path`.
- Each entry may contain `settings` and/or `pull-request`.

`pull-request`

- Optional.
- Used only when running `repo-conventions apply --open-pr`.
- Supports `labels`, `reviewers`, `assignees`, `draft`, `auto-merge`, and `merge-method`.

### Convention Paths

Use one of these path forms.

`owner/repo/path@ref`

- Resolves from a GitHub repository.
- `path` is optional; omitting it targets the repository root.
- `@ref` is optional; omitting it uses the remote repository's default branch.

`./relative/path` or `../relative/path` or `/root/relative/path`

- Resolves relative to the YAML file that contains the reference, or from the root of its repository.
- Useful for conventions stored in the same repository.

### Convention Settings

`settings` is passed to the convention as a JSON-compatible object, i.e. named properties with strings, numbers, Booleans, objects, arrays, and/or null.

Example:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk
    settings:
      version: 10
```

### Pull Request Settings

Both the repository-level configuration and individual convention entries can contribute PR settings, which are used when `--open-pr` opens a PR.

Supported properties:

- `auto-merge`
- `merge-method`
- `reviewers`
- `assignees`
- `draft`
- `labels`

Example:

```yaml
conventions:
  - path: ./conventions/update-dependencies
    pull-request:
      reviewers:
        - my-org/dotnet-team
      labels:
        - dependencies

pull-request:
  auto-merge: true
  merge-method: squash
  labels:
    - automation
```

Notes:

- Convention-level PR settings are only relevant when that convention actually contributes commits.
- RepoConventions always applies a `repo-conventions` label to generated PRs.
- When `draft` is `true`, pull requests are created as drafts.
- When auto-merge is enabled, reviewers and assignees are not requested on PR creation.
- CLI flags can override configured pull request metadata for a single run.
- Note that convention definitions can provide default `pull-request` settings; read the convention definition or documentation for details.

## CLI Commands

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

### CLI Path Overrides

These options affect how the CLI locates the target repository and conventions file:

- `--repo <path>` resolves the target repository root relative to the current process directory.
- `--config <path>` resolves relative to the target repository root and defaults to `.github/conventions.yml`.
- `--temp <path>` resolves relative to the target repository root and defaults to the system temp directory.

RepoConventions uses relative path forms in status and error messages when that keeps output shorter.

### CLI Requirements

- Run from the Git repository root.
- `apply` requires a clean working tree.
- `add --open-pr` and `apply --open-pr` require a clean working tree, a non-detached branch, and no unpushed commits.
- PR automation expects the GitHub CLI to be available and authenticated.

## Related Docs

- See [../README.md](../README.md) for the quick start.
- See [authoring-conventions.md](authoring-conventions.md) for writing conventions.
