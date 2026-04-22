# YAML Settings File Readers

## Status

Proposed.

## Purpose

Allow YAML-authored convention settings to populate values from external files without requiring a wrapper script.

The initial motivating case is:

```yaml
conventions:
  - path: ./conventions/example
    settings:
      text: ${{ readText(settings.foo.bar) }}
```

where the resolved file is read as UTF-8 text and assigned to `text`.

This plan also covers `readJson` and `readYaml` so structured values can be loaded from files with the same relative-path and missing-value semantics.

## Goals

- Support `${{ readText(expr) }}`, `${{ readJson(expr) }}`, and `${{ readYaml(expr) }}` in YAML-authored settings.
- Allow `expr` to be either a dotted `settings.foo.bar` path or a JSON string literal such as `"./fragments/body.md"`.
- Resolve relative file paths against the directory containing the YAML file that authored the expression.
- Preserve existing missing-value behavior: if the input property is missing, treat the function result as missing.
- Treat a missing target file the same as a missing property.
- Preserve typed replacement behavior for exact-value expressions so `readJson` and `readYaml` can produce objects, arrays, numbers, booleans, or null.
- Keep the expression language intentionally small and validation errors explicit.

## Non-Goals

- General-purpose expression syntax, operators, defaults, or conditionals.
- Arbitrary functions beyond `readText`, `readJson`, and `readYaml` in this iteration.
- File writes, globbing, directory traversal helpers, or environment-variable expansion.
- Supporting single-quoted string literals or non-JSON string literal syntax.

## Proposed Syntax

Supported forms:

```text
${{ readText(settings.foo.bar) }}
${{ readText("./body.md") }}
${{ readJson(settings.templates.releaseNotes) }}
${{ readJson("./data/release.json") }}
${{ readYaml(settings.fragments.shared) }}
${{ readYaml("../common/config.yml") }}
```

Rules:

- Function names are case-sensitive and initially limited to `readText`, `readJson`, and `readYaml`.
- Each function accepts exactly one argument.
- The argument is either:
  - a `settings.` dotted property path using the existing segment rules, or
  - a JSON string literal parsed with JSON escaping rules.
- Optional whitespace is allowed inside `${{ ... }}` and around the function argument.
- Nested function calls are not supported.
- Additional arguments are not supported.

Examples that should remain invalid:

```text
${{ readText() }}
${{ readText(settings.foo, settings.bar) }}
${{ readText('body.md') }}
${{ readText(readText("a.txt")) }}
${{ readText(settings.items[0]) }}
```

## Scope Of Evaluation

The current implementation only evaluates expressions when composite convention child settings are propagated.

This plan broadens evaluation so YAML-authored settings are processed consistently for every convention reference loaded from a YAML file:

- top-level `.github/conventions.yml`
- composite `convention.yml`

That keeps relative-path behavior coherent because every expression is evaluated with the path of the YAML file that contains it.

`settings.foo.bar` lookups still require parent settings and remain meaningful primarily during composite expansion. File-reader functions, however, should work anywhere YAML settings are allowed.

## Path Resolution

Every evaluation must know the absolute path of the YAML file currently being processed.

Path resolution rules:

- If the function argument resolves to an absolute path, use it as-is.
- If it resolves to a relative path, combine it with the directory containing the YAML file that authored the expression.
- Normalize the combined path before opening the file.
- Do not resolve relative paths against the repository root, current working directory, or convention script directory.

Examples:

- In `.github/conventions.yml`, `${{ readText("./templates/pr.md") }}` reads `.github/templates/pr.md`.
- In `.github/conventions/parent/convention.yml`, `${{ readYaml("../common/shared.yml") }}` resolves relative to `.github/conventions/parent`.
- In `.github/conventions/parent/convention.yml`, `${{ readText(settings.paths.body) }}` uses the string value found in `settings.paths.body`, then resolves that path relative to `.github/conventions/parent` if it is not absolute.

## Resolution Semantics

### Argument Resolution

1. Parse the function call.
2. Resolve the single argument.
3. If the argument is a `settings.` path and that property is missing, the whole function result is missing.
4. If the argument resolves to `null`, a non-string JSON value, or an object/array, fail with a clear validation error because a file path must resolve to a string.
5. If the argument resolves to a string, treat that string as the file path.

### File Loading

- `readText(path)` reads the target file as UTF-8 text and returns a string.
- `readJson(path)` reads UTF-8 JSON and returns the parsed JSON value.
- `readYaml(path)` reads UTF-8 YAML, converts it through the existing YAML-to-JSON pipeline, and returns the parsed JSON value.

### Missing Inputs

The following cases all produce a missing result rather than a hard failure:

- the `settings.` argument path is missing
- the resolved file path does not exist

Missing results follow the existing exact-value semantics:

- exact-value object property: omit the property
- exact-value array item: omit the item
- embedded string replacement: insert the empty string

### Invalid Inputs

The following cases should remain hard failures with a precise error message that names the convention, YAML location, and raw expression:

- invalid expression syntax
- unsupported argument form
- argument resolved to a non-string value
- `readJson` target exists but contains invalid JSON
- `readYaml` target exists but contains invalid YAML
- traversal through a non-object value in a `settings.` path

## Implementation Plan

1. Generalize the current evaluator contract.
Add an evaluation context object that carries the current YAML file path, its containing directory, the optional parent settings object, the source convention name, and the child convention path. Replace the current `expandCompositeSettings` boolean split in [src/RepoConventions/ConventionRunner.cs](../src/RepoConventions/ConventionRunner.cs) with one evaluator path that can always process YAML-authored expressions.

2. Extend expression parsing in [src/RepoConventions/ConventionSettingsPropagator.cs](../src/RepoConventions/ConventionSettingsPropagator.cs).
Keep the existing literal and exact-expression handling, but add a parsed representation for function-call expressions. Reuse the existing `settings.` path parser for dotted-property arguments and add a small JSON-string-literal parser for quoted path arguments.

3. Introduce a focused file-loading helper.
Add a helper such as `ConventionSettingsFileReader` that takes the evaluation context plus a resolved string path and returns a `JsonNode?` result plus a missing/not-missing signal. Keep file I/O and format parsing out of the core string-segmentation logic so the evaluator remains testable.

4. Reuse existing YAML conversion rules.
Implement `readYaml` by routing through the same YamlDotNet-to-JSON conversion pattern already used in [src/RepoConventions/ConventionConfiguration.cs](../src/RepoConventions/ConventionConfiguration.cs). That keeps YAML scalar and mapping behavior aligned with current configuration loading.

5. Preserve exact-value and embedded-string behavior.
For exact expressions, return the loaded value as its native JSON type. For embedded strings, allow `readText` to interpolate as text and stringify `readJson` or `readYaml` results the same way existing embedded expressions stringify structured values.

6. Thread YAML file identity through recursive planning.
When [src/RepoConventions/ConventionRunner.cs](../src/RepoConventions/ConventionRunner.cs) loads a nested `convention.yml`, pass that file path into subsequent settings evaluation so each file-reader expression resolves relative to the YAML file that contains it, not the original top-level config.

7. Add targeted tests in [tests/RepoConventions.Tests/ConventionExecutionTests.cs](../tests/RepoConventions.Tests/ConventionExecutionTests.cs).
Cover:
  - `readText` from a literal relative path
  - `readText` from a `settings.` path
  - `readJson` exact-value replacement preserving objects and arrays
  - `readYaml` exact-value replacement preserving objects and arrays
  - relative-path resolution in top-level config versus nested composite config
  - missing `settings.` argument path omitting the property
  - missing target file omitting the property
  - invalid JSON/YAML file contents failing before any convention is applied
  - non-string resolved path value failing with a clear error

8. Document the new syntax.
Update the relevant user-facing docs after implementation so the supported expression forms, missing-file behavior, and YAML-relative path rule are explicit.

## Suggested Delivery Order

1. Refactor the evaluator contract to carry configuration-file path context without changing behavior.
2. Add parser support for function-call expressions plus tests for parse and validation failures.
3. Implement `readText` and the missing-file semantics.
4. Implement `readJson` and `readYaml` on top of the same helper.
5. Add top-level and nested relative-path coverage.
6. Update docs.

## Questions And Concerns

- Scope question: should these file-reader functions work only during composite child-settings expansion, or everywhere YAML-authored settings are accepted? This plan assumes everywhere for consistency with the YAML-relative path requirement.
- Embedded-string question: `readText` fits naturally inside larger strings, but `readJson` and `readYaml` only make sense there if JSON stringification is acceptable. If that feels too implicit, we should limit file-reader functions to exact-value expressions in the first version.
- Type question: this plan treats a resolved non-string path value as an error, not as a missing value. That is stricter and should catch author mistakes earlier.
- Encoding question: this plan assumes UTF-8 and does not attempt BOM detection beyond what the .NET text readers already handle. If broader encoding support matters, that should be called out explicitly.
- Security question: this plan intentionally allows paths outside the repository when an absolute path or enough `..` segments are provided. If that is undesirable, we should decide on a confinement rule before implementation.
