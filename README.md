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

Add `.github/conventions.yml` to the repository you want to manage.

```yaml
pull-request:
  labels:
    - automation

conventions:
  - path: Faithlife/CodingGuidelines/conventions/dotnet-sdk@v1
    settings:
      version: 10
  - path: ./conventions/local-policy
```

Run from the repository root.

```pwsh
repo-conventions apply
repo-conventions apply --open-pr
```

## Configuration

RepoConventions reads `.github/conventions.yml`.

- `conventions` is required and lists convention references in application order.
- Each convention entry must have `path` and may have `settings` and `pull-request`.
- Top-level `pull-request` config controls metadata for PRs opened by `repo-conventions apply --open-pr`.

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
- avoid detached HEAD
- avoid unpushed local commits on the current branch

You can also override PR behavior for a run with:

- `--auto-merge`
- `--no-auto-merge`
- `--merge-method merge|squash|rebase`

## More Documentation

- [Detailed configuration](docs/configuration.md)
- [Authoring published conventions](docs/authoring-conventions.md)
