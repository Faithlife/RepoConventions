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

## Features

- [**Repository Configuration**](/skills/repo-conventions/references/repository-configuration.md) - Learn how to configure `.github/conventions.yml`, declare local and remote conventions, pass settings, and configure pull request metadata.
- [**CLI Reference**](/skills/repo-conventions/references/cli-reference.md) - Learn how to use `add`, `validate`, and `apply`, including path options, pull request options, and clean-repository requirements.
- [**Convention Configuration**](/skills/repo-conventions/references/convention-configuration.md) - Learn the shared YAML model for convention references, `convention.yml`, commit settings, pull request settings, and child settings expressions.
- [**Convention Authoring**](/skills/repo-conventions/references/convention-authoring.md) - Learn how to create idempotent convention directories with scripts, local documentation, tests, predictable output, and safe failure behavior.
