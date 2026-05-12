# RepoConventions

Applies shared repository conventions.

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/repo-conventions.svg)](https://www.nuget.org/packages/repo-conventions)

## Quick Start

Install the `repo-conventions` tool globally:

```pwsh
dotnet tool install --global repo-conventions
```

Or run it ad hoc with `dnx`, e.g.

```pwsh
dnx --yes repo-conventions apply
```

Create `.github/conventions.yml` in the repository you want to manage. Use `repo-conventions add` to quickly create the file with the specified convention.

For example, to add a convention that uses `.gitattributes` to enforce LF linefeeds:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

This is the `.github/conventions.yml` that will have been created:

```yaml
conventions:
  - path: Faithlife/CodingGuidelines/conventions/gitattributes-lf
```

Commit `.github/conventions.yml`, then run `repo-conventions apply` from the repository root to apply the convention.

Alternatively, use `--open-pr` to add the reference, apply it, and open a PR in one run:

```pwsh
repo-conventions add Faithlife/CodingGuidelines/conventions/gitattributes-lf --open-pr
```

## Configuration

RepoConventions reads `.github/conventions.yml`.

- `conventions` is required and lists convention references in the order they are applied.
- Each convention entry must have `path` and may have `settings`.
- Use `pull-request` to control the settings of any PR opened by `repo-conventions apply --open-pr`.

Regarding convention paths:

- Remote paths use `owner/repo/path@ref`. If `path` is omitted, the repository root is used. If `@ref` is omitted, the default branch is used.
- Local relative paths start with `./` or `../` and are resolved relative to the configuration file, or start with `/` and are resolved from the repository root.
- Conventions usually have a README that documents what they do and any settings they may support.

## More Documentation

- [Configuration Reference](docs/configuration.md)
- [Authoring Conventions](docs/authoring-conventions.md)
