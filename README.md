# RepoConventions

Applies shared repository conventions.

[![Build](https://github.com/Faithlife/RepoConventions/workflows/Build/badge.svg)](https://github.com/Faithlife/RepoConventions/actions?query=workflow%3ABuild) [![NuGet](https://img.shields.io/nuget/v/RepoConventions.svg)](https://www.nuget.org/packages/RepoConventions)

## Usage

To apply shared conventions to a repository, add a conventions configuration file at `.github/conventions.yml`. The YAML in that file should have a `conventions` property with one or more convention objects. Each convention object has a `path` and optional `settings`. For example:

```yaml
conventions:
  - path: my-org/my-repo/my-convention@v1
    settings:
      my-flag: true
  - path: my-org/my-repo/another-convention
```

Use the `repo-conventions` CLI (e.g. via GitHub workflow) to apply the conventions to the repository and create a pull request if any changes are needed.

```pwsh
dnx repo-conventions --open-pr
```

### Convention paths

A remote convention path mirrors the `owner/repo/path@ref` style used by GitHub Actions, pointing to a convention directory in the specified branch/tag/commit of the specified GitHub repository. If the path is omitted, the root of the repository is used. If `@ref` is omitted, the head of the default branch is used.

A local convention path starts with `./` or `../`. It is relative to the directory containing the configuration file with the path, pointing to a convention directory in the same commit of the same GitHub repository.

### Convention definitions

A convention directory contains a convention definition, which consists of a convention configuration file or a convention script file, as well as any optional supporting files, such as a `README.md`, or files used by the convention script.

#### Composite conventions

A convention with a configuration file is a composite convention. The configuration file is named `convention.yml`. The YAML in that file should have a `conventions` property with one or more convention objects. Each convention object has a `path` and optional `settings`. When such a convention is applied, all of the conventions in the convention configuration file are applied.

#### Executable conventions

A convention with a script file is an executable convention. The script file is named `convention.ps1`. When the convention is applied, the script is run from the root directory of the target Git repository. The first and only argument passed to the script is the path to a JSON file. The JSON file contains an object with a `settings` property, which is set to the settings specified with the convention path, if any.

The convention script should check the repository to see if it adheres to the convention. If it does, it should return with a zero exit code. If it does not, it should make changes to the repository that bring the repository into compliance. The script can create commits, but if it leaves tracked or untracked file changes in the working set, they will automatically be committed. If the script cannot bring the repository into compliance, it returns a non-zero exit code, in which case any changes made by the script to the repository will be reverted.

### Repo Conventions CLI

The `repo-conventions` CLI can be installed as a .NET tool or run with `dnx`.

When run with no arguments, the CLI validates the conventions configuration file for the target repository and ensures that the target repository is ready for fixes, i.e. it has no staged changes, no unstaged tracked changes, and no untracked non-ignored files.

When run with a `--commit` or `--open-pr` argument, each executable convention is run in turn.

With `--open-pr`, the CLI then opens a PR if the executable conventions resulted in any commits.
