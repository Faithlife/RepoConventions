# YAML Settings File Reading

## Status

Proposed.

## Purpose

Allow YAML-authored convention settings to populate values from external files without requiring a wrapper script.

The initial motivating case is:

```yaml
conventions:
  - path: ./conventions/example
    settings:
      text: ${{ readText("./body.md") }}
```

where the resolved file is read as UTF-8 text and assigned to `text`.

## Goals

- Support `${{ readText(expr) }}` in YAML-authored settings.
- Require `expr` to be a JSON string literal such as `"./fragments/body.md"`.
- Resolve relative file paths against the directory containing the YAML file that authored the expression.
- Fail if the target file is missing.
- Strip a UTF-8 BOM when present and otherwise require UTF-8 text.
- Prevent relative paths from escaping the repository root.
- Keep the expression language intentionally small and validation errors explicit.

## Non-Goals

- General-purpose expression syntax, operators, defaults, or conditionals.
- Additional file-reader functions such as `readJson` or `readYaml` in this iteration.
- File writes, globbing, directory traversal helpers, or environment-variable expansion.
- `settings.` expressions as arguments to `readText`.
- Supporting single-quoted string literals or non-JSON string literal syntax.

## Proposed Syntax

Supported forms:

```text
${{ readText("./body.md") }}
${{ readText("../common/body.md") }}
${{ readText("docs/release-notes.txt") }}
```

Rules:

- Function names are case-sensitive and initially limited to `readText`.
- Each function accepts exactly one argument.
- The argument must be a JSON string literal parsed with JSON escaping rules.
- Optional whitespace is allowed inside `${{ ... }}` and around the function argument.
- Nested function calls are not supported.
- Additional arguments are not supported.

Examples that should remain invalid:

```text
${{ readText() }}
${{ readText(settings.foo.bar) }}
${{ readText('body.md') }}
${{ readText(readText("a.txt")) }}
${{ readJson("a.json") }}
```

## Scope Of Evaluation

The current implementation only evaluates expressions when composite convention child settings are propagated.

This plan broadens evaluation so YAML-authored settings are processed consistently for every convention reference loaded from a YAML file:

- top-level `.github/conventions.yml`
- composite `convention.yml`

That keeps relative-path behavior coherent because every expression is evaluated with the path of the YAML file that contains it.

## Path Resolution

Every evaluation must know the absolute path of the YAML file currently being processed.

Every relative-path check must also know the repository root so the implementation can reject relative paths that escape it.

Path resolution rules:

- Parse the function argument as a JSON string literal.
- If the parsed path is relative, combine it with the directory containing the YAML file that authored the expression.
- Normalize the combined path before opening the file.
- If the original argument was relative and the normalized result falls outside the repository root, fail.
- Absolute paths are allowed as-is.
- Do not resolve relative paths against the repository root, current working directory, or convention script directory.

Examples:

- In `.github/conventions.yml`, `${{ readText("./templates/pr.md") }}` reads `.github/templates/pr.md`.
- In `.github/conventions/parent/convention.yml`, `${{ readText("../common/shared.md") }}` resolves relative to `.github/conventions/parent`.
- In `.github/conventions/parent/convention.yml`, `${{ readText("../../../outside.txt") }}` fails if the normalized path escapes the repository root.

## Resolution Semantics

### Argument Resolution

1. Parse the function call.
2. Parse the single argument as a JSON string literal.
3. If parsing fails, report a validation error.
4. Treat the parsed string as the file path.

### File Loading

- `readText(path)` reads the target file as UTF-8 text, strips a leading UTF-8 BOM if present, and returns the resulting string.

### Missing Inputs

There are no missing-input semantics for `readText` in this version.

If the expression is present and the target file does not exist, evaluation fails.

### Invalid Inputs

The following cases should remain hard failures with a precise error message that names the convention, YAML location, and raw expression:

- invalid expression syntax
- unsupported argument form
- malformed JSON string literal argument
- target file does not exist
- relative path escapes the repository root
- file contents are not valid UTF-8 text

## Implementation Plan

1. Generalize the current evaluator contract.
Add an evaluation context object that carries the current YAML file path, its containing directory, the repository root, the source convention name, and the child convention path. Replace the current `expandCompositeSettings` boolean split in [src/RepoConventions/ConventionRunner.cs](../src/RepoConventions/ConventionRunner.cs) with one evaluator path that can always process YAML-authored expressions.

2. Extend expression parsing in [src/RepoConventions/ConventionSettingsPropagator.cs](../src/RepoConventions/ConventionSettingsPropagator.cs).
Keep the existing literal and exact-expression handling, but add a parsed representation for `readText` calls whose single argument is a JSON string literal. Reject `settings.` paths and any other argument form during parsing.

3. Introduce a focused file-loading helper.
Add a helper such as `ConventionSettingsFileReader` that takes the evaluation context plus a string path, resolves it, enforces repository confinement for relative paths, reads BOM-stripped UTF-8 text, and returns a string value. Keep file I/O out of the core string-segmentation logic so the evaluator remains testable.

4. Preserve exact-value and embedded-string behavior.
For exact expressions, return the loaded text as a JSON string value. For embedded strings, insert the loaded text directly.

5. Thread YAML file identity through recursive planning.
When [src/RepoConventions/ConventionRunner.cs](../src/RepoConventions/ConventionRunner.cs) loads a nested `convention.yml`, pass that file path into subsequent settings evaluation so each file-reader expression resolves relative to the YAML file that contains it, not the original top-level config.

6. Add targeted tests in [tests/RepoConventions.Tests/ConventionExecutionTests.cs](../tests/RepoConventions.Tests/ConventionExecutionTests.cs).
Cover:
  - `readText` from a literal relative path
  - relative-path resolution in top-level config versus nested composite config
  - missing target file failing before any convention is applied
  - malformed JSON string literal argument failing with a clear error
  - relative path escaping the repository root failing with a clear error
  - UTF-8 BOM stripping
  - invalid UTF-8 contents failing with a clear error

7. Document the new syntax.
Update the relevant user-facing docs after implementation so the supported expression forms, missing-file behavior, and YAML-relative path rule are explicit.

## Suggested Delivery Order

1. Refactor the evaluator contract to carry configuration-file path context without changing behavior.
2. Add parser support for function-call expressions plus tests for parse and validation failures.
3. Implement `readText`, BOM-stripped UTF-8 loading, and missing-file failure.
4. Add repository-confinement checks for relative paths.
5. Add top-level and nested relative-path coverage.
6. Update docs.

## Questions And Concerns

- Repository-boundary concern: the implementation needs one stable definition of repository root for nested convention execution. It should use the root of the repo being processed, not the current working directory.
- Absolute-path concern: this plan only forbids repository escape for relative paths. If absolute paths should also be blocked, that is a separate policy decision.
- Embedded-string concern: multiline file content embedded into a larger scalar is allowed by this plan, which is simple but may surprise authors if they interpolate large files into short labels or messages.
