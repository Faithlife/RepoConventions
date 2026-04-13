# RepoConventions

Applies shared repository conventions.

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/RepoConventions.svg)](https://www.nuget.org/packages/RepoConventions)

## Usage

Add a conventions configuration file at `.github/conventions.yml`. The file must contain a `conventions` property with one or more convention objects. Each convention object has a `path` and optional `settings`. For example:

```yaml
conventions:
  - path: my-org/my-repo/my-convention@v1
    settings:
      my-flag: true
  - path: my-org/my-repo/another-convention
```

Run the CLI as a .NET tool or with `dnx`.

```pwsh
dnx repo-conventions --open-pr
```

Convention paths use one of these forms:

- `owner/repo/path@ref` for a convention in a GitHub repository. `path` and `@ref` are optional.
- `./path` or `../path` for a convention relative to the configuration file.

For implementation details and open design questions, see [docs/tech-spec.md](docs/tech-spec.md).
