# RepoConventions

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/repo-conventions.svg)](https://www.nuget.org/packages/repo-conventions)

RepoConventions is a .NET tool that runs convention scripts configured for a repository, committing any resulting changes and optionally opening a GitHub pull request.

## Quick Start

Install the tool globally:

```pwsh
dotnet tool install --global repo-conventions
```

Or run it ad hoc with `dnx`:

```pwsh
dnx repo-conventions
```

To add a convention to the current repository:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

That command creates `.github/conventions.yml` if it does not already exist:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

Commit the configuration file, then apply the convention from the repository root:

```pwsh
repo-conventions apply
```

To add the reference, apply it, commit the changes, push a branch, and open a GitHub pull request in one run:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/gitattributes-lf --open-pr
```

## Configuration

RepoConventions reads `.github/conventions.yml` from the target repository root by default. Use `--config <path>` to use a different file. Custom config paths are resolved relative to the target repository root.

A configuration file is a YAML mapping with these top-level properties:

| Property | Required | Description |
| --- | --- | --- |
| `conventions` | Yes | Sequence of convention references, applied in declaration order. |
| `pull-request` | No | Default pull request metadata used only when running with `--open-pr`. |

Complete example:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk@v1
    settings:
      version: 10
  - path: ./conventions/local-policy
    pull-request:
      labels:
        - dependencies

pull-request:
  labels:
    - automation
  reviewers:
    - octocat
  assignees:
    - octocat
  draft: true
  auto-merge: false
  merge-method: squash
```

### Convention References

Each item in `conventions` must contain a non-empty `path`. It may also contain `settings` and `pull-request`.

`path` identifies a convention directory. That directory must contain `convention.yml`, `convention.ps1`, or both. If both files exist, RepoConventions applies child conventions from `convention.yml` before running `convention.ps1`.

Supported path forms:

| Form | Meaning |
| --- | --- |
| `owner/repo/path@ref` | Clone a GitHub repository and use `path` inside it. `path` may be omitted to use the repository root. `@ref` may be omitted to use the repository default branch. |
| `./relative/path` | Resolve relative to the YAML file that contains the reference. |
| `../relative/path` | Resolve relative to the YAML file that contains the reference. The result must stay inside that YAML file's repository. |
| `/root/relative/path` | Resolve from the root of the repository that contains the YAML file. |

`settings` is passed to the convention as JSON-compatible data. Use YAML objects, arrays, strings, numbers, booleans, or null values. Each convention documents the settings it accepts.

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk
    settings:
      version: 10
```

### Pull Request Settings

Pull request settings are used only when the command runs with `--open-pr`.

Supported properties:

| Property | Type | Description |
| --- | --- | --- |
| `labels` | string sequence | Labels to add to the generated pull request. Missing labels are created automatically. RepoConventions always also adds the `repo-conventions` label. |
| `reviewers` | string sequence | GitHub users or teams to request as reviewers. Team reviewers use GitHub's `org/team` form. |
| `assignees` | string sequence | GitHub users to assign. |
| `draft` | boolean | When true, create the pull request as a draft. |
| `auto-merge` | boolean | When true, enable GitHub auto-merge after opening or updating the pull request. |
| `merge-method` | string | Preferred auto-merge method: `merge`, `squash`, or `rebase`. Defaults to `squash` when auto-merge is enabled and no single method is configured. |

Pull request settings can appear at three levels:

- Top-level `pull-request` settings apply to the whole generated pull request.
- A top-level convention reference's `pull-request` settings apply only if that convention contributes commits to the generated pull request.
- A local convention's own `convention.yml` can provide default `pull-request` settings for that convention. Reference-level settings supplement list values and override scalar values. Pull request settings from convention definitions cloned from remote repositories are ignored; put desired PR metadata in the consuming repository's configuration instead.

RepoConventions de-duplicates labels, reviewers, and assignees case-insensitively while preserving the first spelling it sees. Convention-level PR metadata is ignored for conventions that do not create commits during the run.

CLI flags override configured PR settings for a single run:

- `--draft` and `--no-draft` override `draft`.
- `--auto-merge` and `--no-auto-merge` override `auto-merge`.
- `--merge-method merge|squash|rebase` overrides `merge-method`.

When auto-merge is enabled, RepoConventions does not request reviewers or assignees. If a requested merge method is disabled or rejected by GitHub, RepoConventions tries other allowed methods, preferring `squash` as the first fallback. If auto-merge was enabled by configuration and cannot be enabled, the command reports the failure but still succeeds. If `--auto-merge` was provided explicitly and auto-merge cannot be enabled, the command fails.

## CLI Reference

RepoConventions has two commands:

```pwsh
repo-conventions add <path> [<path>...] [options]
repo-conventions apply [options]
```

Common path options:

| Option | Description |
| --- | --- |
| `--repo <path>` | Target repository root. Defaults to the current directory. Relative paths are resolved from the current process directory. |
| `--config <path>` | Conventions configuration file. Defaults to `.github/conventions.yml` under the target repository root. Relative paths are resolved from the target repository root. |
| `--temp <path>` | Temporary root for RepoConventions-managed transient files. Defaults to the system temp directory. Relative paths are resolved from the target repository root. |

Common pull request options:

| Option | Description |
| --- | --- |
| `--open-pr` | Apply conventions, create commits, push a `repo-conventions` branch, and open or update a pull request. |
| `--draft` / `--no-draft` | Override configured draft behavior. These options cannot be used together. |
| `--auto-merge` / `--no-auto-merge` | Override configured auto-merge behavior. These options cannot be used together. |
| `--merge-method <method>` | Preferred auto-merge method. Must be `merge`, `squash`, or `rebase`. |
| `--git-no-verify` | Pass `--no-verify` to `git commit` and `git push` commands run by RepoConventions. |

### `add`

`repo-conventions add` appends one or more convention paths to the configuration file. If the file is missing, it creates it. If a path is already present, it leaves the file unchanged for that path. If settings are required, they must be added by hand to the configuration file.

Examples:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/dotnet-sdk
repo-conventions add ./conventions/local-policy
repo-conventions add ./conventions/dotnet-sdk ./conventions/github-actions
repo-conventions add ./conventions/local-policy --repo ../target-repo --config .config/repo-conventions.yml
```

`add` requires the target path to be a Git repository root, but it does not require a clean working tree unless `--open-pr` is used.

With `--open-pr`, `add` commits any newly added convention references, applies the resulting configuration, commits convention changes, and opens or updates a pull request:

```pwsh
repo-conventions add ./conventions/local-policy --open-pr --git-no-verify
```

### `apply`

`repo-conventions apply` loads the configuration file, resolves the full convention plan, applies each convention in order, and creates commits for conventions that leave changes behind.

Examples:

```pwsh
repo-conventions apply
repo-conventions apply --git-no-verify
repo-conventions apply --repo ../target-repo --config .config/repo-conventions.yml --temp .artifacts/repo-conventions-temp
```

`apply` requires the target repository to be clean before it starts. If a convention script fails, RepoConventions resets the target repository back to the commit before that convention started and stops the run.

With `--open-pr`, `apply` pushes convention commits and opens or updates a GitHub pull request:

```pwsh
repo-conventions apply --open-pr
repo-conventions apply --open-pr --draft
repo-conventions apply --open-pr --no-draft
repo-conventions apply --open-pr --auto-merge --merge-method rebase
repo-conventions apply --open-pr --no-auto-merge
```

`--open-pr` requires:

- a clean target repository
- a non-detached starting branch
- no unpushed commits on the starting branch
- the GitHub CLI `gh` installed and authenticated

When opening a pull request, RepoConventions creates `repo-conventions`, `repo-conventions-2`, or the next available suffix. If an open RepoConventions pull request already targets the starting branch, the command updates that pull request instead of opening another one. If the base branch has advanced, the existing PR branch is rebuilt from the current base and force-pushed.

## Commits and Convention Execution

Convention scripts run with `pwsh -NoProfile` from the target repository root. Each script receives one argument: the path to a JSON file with a single `settings` property.

When a script exits successfully, RepoConventions commits any tracked or untracked changes left by the script using `Apply convention <name>`. If the script creates its own commits, those commits are preserved. If no changes or commits are created, no commit is added for that convention.

When running in GitHub Actions, command output is grouped per convention and the final summary line is also appended to `GITHUB_STEP_SUMMARY` when that environment variable is available.

## Writing Conventions

Conventions consumed by this tool are directories containing `convention.yml`, `convention.ps1`, or both. For the authoring contract, examples, idempotency expectations, and agent-friendly workflow, see [skills/create-repo-conventions/SKILL.md](skills/create-repo-conventions/SKILL.md).
