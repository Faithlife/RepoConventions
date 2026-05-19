# RepoConventions

[![NuGet](https://img.shields.io/nuget/v/repo-conventions.svg)](https://www.nuget.org/packages/repo-conventions)

RepoConventions is a .NET tool that runs convention scripts configured for a repository, committing any resulting changes and optionally opening a GitHub pull request.

> [!CAUTION]
> This is an inherently dangerous tool. It runs PowerShell scripts from arbitrary GitHub repositories, potentially with full access to local files and production secrets, with the capability of automatically merging pull requests. Only apply conventions from trusted sources. Make sure that conventions from trusted sources don't reference conventions from untrusted sources. Convention paths that are not pinned to a specific SHA are subject to supply chain attacks, so make sure convention repositories are secure and that contributions are carefully reviewed.

## Quick Start

Before running RepoConventions, make sure you have installed:

- `dotnet`: .NET 10 SDK or later
- `pwsh`: PowerShell 7 or later
- `git`: Git CLI, configured and authenticated
- `gh`: GitHub CLI, configured and authenticated (for opening PRs)

Individual conventions may require additional tools.

Run the tool with `dnx`:

```pwsh
dnx repo-conventions --help
```

If you prefer, or to run in a repository configured for .NET 8, install the tool globally and run it without using `dnx`:

```pwsh
dotnet tool install -g repo-conventions
repo-conventions --help
```

To add a convention to the current repository:

```pwsh
dnx repo-conventions add Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

That command creates `.github/conventions.yml` if it does not already exist:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

Commit the configuration file, then apply the convention from the repository root:

```pwsh
dnx repo-conventions apply
```

## Configuration

RepoConventions reads `.github/conventions.yml` from the target repository root by default.

A configuration file is a YAML file with these top-level properties:

| Property | Required | Description |
| --- | --- | --- |
| `conventions` | Yes | Sequence of convention references, applied in declaration order. |
| `pull-request` | No | Pull request settings; used when running with `--open-pr`. |

Complete example:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk
    settings:
      version: 10
  - path: ./conventions/local-policy
    commit:
      message: Update local policy files
    pull-request:
      labels:
        - dependencies
      auto-merge: false

pull-request:
  labels:
    - automation
  reviewers:
    - octocat
  merge-method: squash
```

### Convention References

Each item in `conventions` must contain a non-empty `path`. It may also contain `settings`, `commit`, and `pull-request`.

`path` identifies a convention directory. Each convention should document its own settings, behavior, and required tools.

Supported path forms:

| Form | Meaning |
| --- | --- |
| `owner/repo/path@ref` | Clone a GitHub repository and use `path` inside it. `path` may be omitted to use the repository root. `@ref` may be omitted to use the default branch. |
| `./relative/path` | Resolve relative to the YAML file that contains the reference. |
| `../relative/path` | Resolve relative to the YAML file that contains the reference. The result must stay inside that YAML file's repository. |
| `/root/relative/path` | Resolve from the root of the repository that contains the YAML file. |

`settings` is passed to the convention as JSON-compatible data. Use YAML objects, arrays, strings, numbers, booleans, or null values. Each convention documents the settings it accepts.

### Commit Settings

Commit settings control the commit created when a convention script leaves uncommitted changes behind.

Supported properties:

| Property | Type | Description |
| --- | --- | --- |
| `message` | string | Commit message to use for the convention's automatic commit. Empty or whitespace-only values are treated as unspecified. |

Commit settings can appear at two levels:

- A convention's `convention.yml` can provide default `commit` settings for that convention.
- A convention reference's `commit` settings override the convention's defaults.

The effective commit message is passed down to child conventions in composite conventions. Child convention definitions and child references can override the inherited message.

If no commit message is configured, RepoConventions uses `Apply convention {name}`. Commit settings do not affect commits created directly by a convention script.

When RepoConventions is about to create an automatic commit with the same message as the previous automatic commit from the same run, it amends that previous commit instead of creating a second adjacent commit with the same message.

### Pull Request Settings

Pull request settings are used when the command runs with `--open-pr`.

Supported properties:

| Property | Type | Description |
| --- | --- | --- |
| `labels` | string sequence | Labels to add to the generated pull request. Missing labels are created automatically. The `repo-conventions` label is always added. |
| `reviewers` | string sequence | GitHub users or teams to request as reviewers. |
| `assignees` | string sequence | GitHub users to assign. |
| `draft` | boolean | When true, create the pull request as a draft. |
| `auto-merge` | boolean | When true, enable GitHub auto-merge after opening the pull request. |
| `merge-method` | string | Preferred auto-merge method: `merge`, `squash`, or `rebase`. Defaults to `squash` when auto-merge is enabled and no single method is configured. |

Pull request settings can appear at three levels:

- Top-level `pull-request` settings apply to the whole generated pull request.
- A top-level convention reference's `pull-request` settings apply only if that convention contributes commits to the generated pull request.
- A convention's own `convention.yml` can provide default `pull-request` settings for that convention, whether the convention is stored in the target repository or cloned from a remote repository. Reference-level settings supplement list values and override scalar values.

RepoConventions de-duplicates labels, reviewers, and assignees case-insensitively while preserving the first spelling it sees. Convention-level PR metadata is ignored for conventions that do not create commits during the run.

CLI flags override configured PR settings for a single run:

- `--draft` and `--no-draft` override `draft`.
- `--auto-merge` and `--no-auto-merge` override `auto-merge`.
- `--merge-method merge|squash|rebase` overrides `merge-method`.

When auto-merge is enabled, RepoConventions does not request reviewers or assignees. If a requested merge method is disabled or rejected by GitHub, RepoConventions tries other allowed methods, preferring `squash` as the first fallback. If auto-merge was enabled by configuration and cannot be enabled, the command reports the failure but still succeeds. If `--auto-merge` was provided explicitly and auto-merge cannot be enabled, the command fails.

## CLI Reference

RepoConventions has three commands:

```pwsh
dnx repo-conventions add <path> [<path>...] [options]
dnx repo-conventions apply [options]
dnx repo-conventions validate [options]
```

Common path options:

| Option | Description |
| --- | --- |
| `--repo <path>` | Target repository root. Defaults to the current directory. Relative paths are resolved from the current process directory. |
| `--config <path>` | Conventions configuration file. Defaults to `.github/conventions.yml` under the target repository root. Relative paths are resolved from the current process directory. |
| `--temp <path>` | Temporary root for RepoConventions-managed transient files. Defaults to the system temp directory. Relative paths are resolved from the current process directory. |

Common pull request options:

| Option | Description |
| --- | --- |
| `--open-pr` | Apply conventions, create commits, push a `repo-conventions` branch, and open or update a pull request. |
| `--draft` / `--no-draft` | Override configured draft behavior. These options cannot be used together. |
| `--auto-merge` / `--no-auto-merge` | Override configured auto-merge behavior. These options cannot be used together. |
| `--merge-method <method>` | Preferred auto-merge method. Must be `merge`, `squash`, or `rebase`. |
| `--git-no-verify` | Pass `--no-verify` to `git commit` and `git push` commands run by RepoConventions. |

### `add`

`repo-conventions add` appends one or more convention paths to the configuration file. If the file is missing, it creates it. If a path is already present, it leaves the file unchanged for that path. New paths are validated before the configuration file is changed. If settings are required, they must be added by hand to the configuration file.

Examples:

```pwsh
dnx repo-conventions add Faithlife/CodingGuidelines/conventions/dotnet-sdk
dnx repo-conventions add ./conventions/local-policy
dnx repo-conventions add ./conventions/dotnet-sdk ./conventions/github-actions
```

`add` requires the target repository path to be a Git repository root. When `--commit`, `--apply`, and `--open-pr` are not used, it can run when the target repository has tracked or untracked file changes.

With `--open-pr`, `add` commits any newly added convention references, applies the resulting configuration, commits convention changes, and opens or updates a pull request:

```pwsh
dnx repo-conventions add ./conventions/local-policy --open-pr
```

### `validate`

`repo-conventions validate` loads the configuration file and resolves the complete convention plan without running convention scripts, creating commits, or changing the working tree.

Examples:

```pwsh
dnx repo-conventions validate
dnx repo-conventions validate --config .config/repo-conventions.yml
```

`validate` requires the target repository path to be a Git repository root. It can run when the target repository has tracked or untracked file changes.

When validation succeeds, it prints a summary with the number of conventions that were validated.

### `apply`

`repo-conventions apply` loads the configuration file, resolves the full convention plan, applies each convention in order, and creates commits for conventions that leave changes behind.

Examples:

```pwsh
dnx repo-conventions apply
dnx repo-conventions apply --git-no-verify
```

`apply` requires no tracked or untracked file changes in the target repository before it starts.

When running in GitHub Actions, RepoConventions groups output per convention and appends the final summary line to `GITHUB_STEP_SUMMARY` when that environment variable is available.

With `--open-pr`, `apply` pushes any convention commits and opens or updates a GitHub pull request:

```pwsh
dnx repo-conventions apply --open-pr
dnx repo-conventions apply --open-pr --draft
dnx repo-conventions apply --open-pr --auto-merge --merge-method rebase
dnx repo-conventions apply --open-pr --no-auto-merge
```

`--open-pr` requires:

- a non-detached starting branch
- no unpushed commits on the starting branch
- the GitHub CLI `gh` installed and authenticated

When opening a pull request, RepoConventions creates a branch named `repo-conventions`, `repo-conventions-2`, or the next available suffix. If an open RepoConventions pull request already targets the starting branch, the command updates that pull request instead of opening another one. If the base branch has advanced, the existing PR branch is rebuilt from the current base and force-pushed.

If applying the conventions produces no commits, RepoConventions returns to the starting branch and does not keep the generated local branch, push a branch, or open a pull request.

## Writing Conventions

For the convention authoring contract, examples, idempotency expectations, script execution details, and agent-friendly workflow, see [skills/create-repo-conventions/SKILL.md](skills/create-repo-conventions/SKILL.md).
