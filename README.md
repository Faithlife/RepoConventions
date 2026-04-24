# RepoConventions

Applies shared repository conventions.

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/repo-conventions.svg)](https://www.nuget.org/packages/repo-conventions)

RepoConventions applies shared repository conventions from `.github/conventions.yml` by default.

## Quick Start

Install the tool globally, or run it ad hoc with `dnx`.

```pwsh
dotnet tool install --global repo-conventions
# or
dnx repo-conventions --help
```

Create `.github/conventions.yml` in the repository you want to manage.

For example, to add a convention that installs a workflow that opens a PR nightly when convention changes are needed:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/repo-conventions-workflow
```

This is the `.github/conventions.yml` that should have been created:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/repo-conventions-workflow
```

Commit `.github/conventions.yml`, then run `repo-conventions apply` from the repository root, which creates and commits the workflow.

## Configuration

RepoConventions reads `.github/conventions.yml` by default, or another file when you pass `--config`.

- `conventions` is required and lists convention references in the order they are applied.
- Each convention entry must have `path` and may have `settings`.
- Use `pull-request` to control the settings of any PR opened by `repo-conventions apply --open-pr`.
- Pull request settings support `labels`, `reviewers`, `assignees`, `draft`, `auto-merge`, and `merge-method`.

Regarding convention paths:

- Remote paths use `owner/repo/path@ref`. If `path` is omitted, the repository root is used. If `@ref` is omitted, the remote default branch is used.
- Local relative paths start with `./` or `../` and are resolved relative to the configuration file, or start with `/` and are resolved from the repository root.

## CLI

`repo-conventions add <path>` adds a convention reference to the configured conventions file, creating it if needed.

`repo-conventions add <path> --repo ../target-repo --config .config/repo-conventions.yml` targets a different repository or config file.

`repo-conventions apply` applies configured conventions and creates commits as needed.

`repo-conventions apply --repo ../target-repo --config .config/repo-conventions.yml --temp .artifacts/repo-conventions-temp` overrides the repository root, config file, and temp root.

`repo-conventions apply --open-pr` applies conventions, creates commits, and opens or updates a PR for any created commits.

When applying conventions, run from the repository root, and start with a clean working tree. With `--open-pr`, there should also be no unpushed local commits.

## More Documentation

- [Detailed configuration](docs/configuration.md)
- [Authoring conventions](docs/authoring-conventions.md)
