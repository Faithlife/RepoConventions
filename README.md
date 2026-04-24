# RepoConventions

Applies shared repository conventions.

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/repo-conventions.svg)](https://www.nuget.org/packages/repo-conventions)

RepoConventions applies shared repository conventions from a `.github/conventions.yml` file.

## Quick Start

Install the tool globally, or run it ad hoc with `dnx`.

```pwsh
dotnet tool install --global repo-conventions
# or
dnx repo-conventions --help
```

Create `.github/conventions.yml` in the repository you want to manage.

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/repo-conventions-workflow
```

That convention installs a workflow that opens a PR nightly when convention changes are needed.

Commit `.github/conventions.yml`, then run from the repository root.

```pwsh
repo-conventions apply
repo-conventions apply --open-pr
```

`repo-conventions apply` creates and commits the workflow after the configuration file is committed.

## Configuration

RepoConventions reads `.github/conventions.yml`.

- `conventions` is required and lists convention references in application order.
- Each convention entry must have `path` and may have `settings`.
- Use `pull-request` to control the settings of the PR opened by `repo-conventions apply --open-pr`.

Convention path forms:

- Local relative paths start with `./` or `../` and are resolved relative to the configuration file.
- Local root-relative paths start with `/` and are resolved from the repository root.
- Remote paths use `owner/repo/path@ref`. If `path` is omitted, the repository root is used. If `@ref` is omitted, the remote default branch is used.

## CLI

`repo-conventions add <path>` adds a convention reference to `.github/conventions.yml`, creating the file if needed.

`repo-conventions apply` applies configured conventions and creates commits as needed.

`repo-conventions apply --open-pr` opens or updates a PR for any generated commits. When using PR automation:

- run from the repository root
- start from a clean working tree
- avoid unpushed local commits on the current branch

You can also override PR behavior for a run with:

- `--auto-merge`
- `--no-auto-merge`
- `--merge-method merge|squash|rebase`

Example:

```yaml
pull-request:
  auto-merge: true

conventions:
  - path: Faithlife/CodingGuidelines/conventions/repo-conventions-workflow
```

## More Documentation

- [Detailed configuration](docs/configuration.md)
- [Authoring published conventions](docs/authoring-conventions.md)
