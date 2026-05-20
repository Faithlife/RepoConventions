# Convention Configuration

RepoConventions uses the same convention reference model in repository configuration and in convention-local `convention.yml` files. A convention directory can also include its own `convention.yml` to compose child conventions, provide default commit settings, provide default pull request settings, or any combination of those.

## Convention References

Each convention reference must contain a non-empty `path`. It may also contain `settings`, `commit`, and `pull-request`.

`path` identifies a convention directory. Each convention should document its own settings, behavior, and required tools.

Supported path forms:

| Form | Meaning |
| --- | --- |
| `owner/repo/path@ref` | Clone a GitHub repository and use `path` inside it. `path` may be omitted to use the repository root. `@ref` may be omitted to use the default branch. |
| `./relative/path` | Resolve relative to the YAML file that contains the reference. |
| `../relative/path` | Resolve relative to the YAML file that contains the reference. The result must stay inside that YAML file's repository. |
| `/root/relative/path` | Resolve from the root of the repository that contains the YAML file. |

Local paths must stay inside the repository that contains the YAML file. This rule applies to conventions checked into the target repository and to convention repositories cloned from GitHub.

`settings` is passed to the convention as JSON-compatible data. Use YAML objects, arrays, strings, numbers, booleans, or null values. Top-level repository configuration uses literal settings values. Composite convention child references can use settings expressions.

## `convention.yml`

Use `convention.yml` when a convention composes other conventions, provides default commit settings, provides default pull request settings, or any combination of those.

Composition-only conventions must include a `conventions` sequence. Executable conventions that also contain `convention.ps1` may omit `conventions` and include only `commit` or `pull-request` settings.

Example:

```yaml
commit:
  message: Update .NET repository conventions

pull-request:
  labels:
    - dependencies
  auto-merge: true
  merge-method: squash

conventions:
  - path: ../dotnet-sdk
    settings:
      version: 10
  - path: ../dotnet-slnx
```

Guidelines:

- Keep child conventions in the order they should be applied.
- Use explicit local relative paths, such as `../dotnet-sdk`, for conventions published from the same repository.
- Keep settings JSON-compatible: objects, arrays, strings, numbers, booleans, or null.
- Keep settings shallow unless nesting communicates a real domain boundary.
- Avoid formatting-only churn in generated files unless formatting is the purpose of the convention.

Supported root properties:

| Property | Type | Description |
| --- | --- | --- |
| `conventions` | sequence | Child convention references to apply in declaration order. Required when the directory has no `convention.ps1`; optional when the convention is executable. |
| `commit` | object | Default automatic commit settings for this convention and its child conventions. |
| `pull-request` | object | Pull request metadata contributed when this convention creates commits. |

## Child Settings Expressions

Composite conventions can map parent settings into child settings with expressions.

`settings` lookup:

```yaml
conventions:
  - path: ../dotnet-sdk
    settings:
      version: ${{ settings.sdk.version }}
```

- Reads a dotted property path from the parent convention's settings object.
- When the whole value is one expression, preserves JSON-compatible types such as strings, numbers, booleans, arrays, objects, and null.
- When embedded in a larger string, converts strings directly, null to `null`, and arrays or objects to compact JSON.
- Missing values are omitted from object properties and array items. If the missing expression is embedded in a larger string, it contributes an empty string.
- If an array expression is used as an array item, its items are spliced into the destination array.

`readText("path")`:

```yaml
conventions:
  - path: ../write-file
    settings:
      body: ${{ readText("./body.txt") }}
```

- Reads UTF-8 text from a file. A UTF-8 BOM is ignored.
- Relative paths resolve from the YAML file that contains the expression.
- Paths beginning with `/` resolve from the root of the repository that contains the YAML file.
- Native absolute paths and paths that escape the containing repository are rejected.
- Use it when file-backed text is clearer than embedding long YAML strings.

## Commit Settings

Commit settings control the automatic commit created when `convention.ps1` leaves uncommitted changes and does not create commits itself.

Supported properties:

| Property | Type | Description |
| --- | --- | --- |
| `message` | string | Commit message for the automatic commit. Empty or whitespace-only values are treated as unspecified. |

Behavior:

- If no message is configured, RepoConventions uses `Apply convention <name>`.
- A convention reference's `commit` settings override the convention's own defaults.
- Composite conventions pass the effective commit message down to child conventions. A child convention's own `commit` settings, or settings on that child reference, can override the inherited message.
- Commit settings do not affect commits created directly by `convention.ps1`.
- When adjacent automatic commits in the same run use the same message, RepoConventions amends the previous automatic commit instead of creating a second adjacent commit with the same message.

Use a custom `message` when the convention has a stable, recognizable purpose. Prefer a concise imperative subject, such as `Update .NET SDK version` or `Refresh generated CI files`.

## Pull Request Settings

Pull request settings describe metadata for the pull request generated from applying conventions. This metadata is used when the command runs with `--open-pr`.

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

- Top-level repository `pull-request` settings apply to the whole generated pull request.
- A convention reference's `pull-request` settings apply only if that convention contributes commits to the generated pull request.
- A convention's own `convention.yml` can provide default `pull-request` settings for that convention, whether the convention is stored in the target repository or cloned from a remote repository.

Merge behavior:

- `labels`, `reviewers`, and `assignees` are appended, then de-duplicated case-insensitively.
- `draft`, `auto-merge`, and `merge-method` are scalar settings; reference-level settings override convention defaults.
- Convention-level pull request metadata is ignored when the convention does not create commits.
- When auto-merge is enabled, reviewers and assignees are not requested.
- If multiple contributing conventions request conflicting merge methods and no repository-level or reference-level setting resolves the conflict, RepoConventions falls back to `squash`.

CLI flags override configured pull request settings for a single run:

- `--draft` and `--no-draft` override `draft`.
- `--auto-merge` and `--no-auto-merge` override `auto-merge`.
- `--merge-method merge|squash|rebase` overrides `merge-method`.

If a requested merge method is disabled or rejected by GitHub, RepoConventions tries other allowed methods, preferring `squash` as the first fallback. If auto-merge was enabled by configuration and cannot be enabled, the command reports the failure but still succeeds. If `--auto-merge` was provided explicitly and auto-merge cannot be enabled, the command fails.
