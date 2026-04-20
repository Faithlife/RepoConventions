# Composite Convention Settings Propagation

## Status

Proposed design for review.

## Purpose

Composite conventions need a way to pass parent settings into child convention settings without requiring code in the composite convention itself. The syntax should feel familiar to users who know GitHub Actions expressions, but the feature should stay intentionally small.

This document defines the first version of settings propagation for composite conventions.

## Goals

- Allow a composite convention to map values from its own effective settings into a child convention's `settings` object.
- Preserve structured values when a child setting is entirely a propagated expression.
- Support simple string interpolation when a propagated expression appears inside a larger string.
- Keep the syntax narrow enough to validate and explain clearly.
- Resolve all propagation during convention plan construction, before any convention is applied.

## Non-Goals

- Full GitHub Actions expression support.
- Access to contexts other than `settings`.
- Functions, operators, conditionals, defaults, or filters.
- Array indexing, wildcards, or bracket notation.
- Interpolation in `path` values or object property names.
- Multiple expressions within one scalar value.

## Terminology

- Parent convention invocation: the convention reference currently being expanded from a composite convention.
- Parent settings: the effective `settings` value attached to that parent convention invocation.
- Child convention reference: one entry under a composite convention's `conventions` list.
- Child settings template: the `settings` value authored on a child convention reference before propagation is resolved.
- Effective settings: the settings value that a convention actually receives after propagation has been resolved.

## Scope

This feature applies only when expanding child convention references from a composite convention's `convention.yml`.

It does not change how top-level `.github/conventions.yml` settings are authored or how executable conventions receive their final JSON input. It only changes how a composite convention computes the effective settings for its children.

## Expression Syntax

Supported syntax:

```text
${{ settings.foo }}
${{ settings.foo.bar }}
${{ settings.foo.bar.baz }}
```

Rules:

- The expression must begin with `settings.`.
- The remainder of the expression must be a single dotted property path.
- Arbitrary path depth is allowed.
- Each path segment refers to an object property name.
- Path segments are treated as literal property names between dots, so names such as `sdk-version` are valid.
- Optional whitespace is allowed immediately inside `${{` and `}}`, matching the usual GitHub Actions style.

Examples of supported expressions:

- `${{ settings.version }}`
- `${{settings.sdk.version}}`
- `${{ settings.test.matrix.os }}`

Examples of unsupported expressions:

- `${{ github.ref }}`
- `${{ settings.foo || 'default' }}`
- `${{ settings.items[0] }}`
- `${{ settings['foo'] }}`
- `${{ settings.foo }}-${{ settings.bar }}`

Notes:

- The path is split on dots. A property whose literal name contains a dot cannot be addressed in this first version.
- The path syntax is intentionally smaller than GitHub Actions. The goal is familiarity, not compatibility with the full expression language.

## Resolution Source

When a composite convention is expanded, every child convention reference resolves expressions against the parent convention invocation's effective settings.

Example:

```yaml
conventions:
  - path: ./conventions/parent
    settings:
      sdk:
        version: 10
```

If `parent/convention.yml` contains:

```yaml
conventions:
  - path: ../child
    settings:
      version: ${{ settings.sdk.version }}
```

Then the child convention's effective settings become:

```yaml
version: 10
```

If that child is itself composite, its own children resolve against this newly computed effective settings object, not the original top-level settings object.

## Traversal Model

Propagation is resolved recursively through the child settings template.

Rules:

- If the child reference has no `settings`, nothing is resolved.
- Objects are traversed value by value.
- Arrays are traversed element by element.
- Object property names are not interpolated.
- Non-string scalar values are left unchanged.
- String scalar values are examined for the propagation syntax defined here.

This allows propagation to appear anywhere inside the child settings value tree, including inside nested objects and arrays.

## Exact-Value Replacement

If a child setting value is a string whose entire contents are exactly one supported expression, the resolved value replaces that string as its original data type.

Examples:

Parent settings:

```yaml
name: repo-conventions
version: 10
enabled: true
labels:
  - automation
  - conventions
metadata:
  owner: Faithlife
nullable: null
```

Child settings template:

```yaml
stringValue: ${{ settings.name }}
numberValue: ${{ settings.version }}
boolValue: ${{ settings.enabled }}
arrayValue: ${{ settings.labels }}
objectValue: ${{ settings.metadata }}
nullValue: ${{ settings.nullable }}
```

Resolved child settings:

```yaml
stringValue: repo-conventions
numberValue: 10
boolValue: true
arrayValue:
  - automation
  - conventions
objectValue:
  owner: Faithlife
nullValue: null
```

This rule is what makes propagation useful for non-string settings. The replacement is typed, not stringified.

## Embedded String Replacement

If a child setting value is a string that contains one supported expression along with other text, the overall result stays a string.

Replacement behavior:

- If the resolved value is a string, insert the raw string value.
- Otherwise, insert the JSON representation of the value.

Examples:

Parent settings:

```yaml
name: repo-conventions
version: 10
enabled: true
labels:
  - automation
  - conventions
metadata:
  owner: Faithlife
nullable: null
```

Child settings template:

```yaml
message: Running ${{ settings.name }}
versionTag: v${{ settings.version }}
enabledMessage: enabled=${{ settings.enabled }}
labelsMessage: labels=${{ settings.labels }}
metadataMessage: metadata=${{ settings.metadata }}
nullMessage: nullable=${{ settings.nullable }}
```

Resolved child settings:

```yaml
message: Running repo-conventions
versionTag: v10
enabledMessage: enabled=true
labelsMessage: labels=["automation","conventions"]
metadataMessage: metadata={"owner":"Faithlife"}
nullMessage: nullable=null
```

For non-string values, the JSON text should be compact and deterministic rather than pretty-printed.

## One Expression Per Scalar

To keep the first version narrow, a single string scalar may contain at most one supported expression.

Supported:

```yaml
value: prefix-${{ settings.name }}
```

Not supported:

```yaml
value: ${{ settings.owner }}/${{ settings.name }}
```

Not supported:

```yaml
value: ${{ settings.owner }}-${{ settings.name }}-${{ settings.version }}
```

This restriction keeps parsing and error reporting simple while still covering the common cases of pass-through and basic string decoration.

## Missing Values And Type Mismatches

A referenced path must resolve successfully through the parent settings object.

Resolution fails if:

- The parent settings object is missing the first requested property.
- An intermediate path segment is missing.
- An intermediate path segment resolves to a non-object before the final segment is reached.

Examples:

- `${{ settings.sdk.version }}` fails if `settings.sdk` is missing.
- `${{ settings.sdk.version }}` fails if `settings.sdk` is the number `10` instead of an object.

`null` is allowed only when the path resolves successfully to a final value of `null`.

That means:

- Exact replacement may produce a real `null` value.
- Embedded string replacement inserts the literal text `null`.

## Error Handling

Settings propagation errors should fail convention plan construction before any convention is applied.

That matches the existing direction that the entire convention plan is built up front.

An error should identify:

- The composite convention being expanded.
- The child convention reference being resolved.
- The child settings location, if practical.
- The expression text.
- The reason the expression could not be resolved.

Representative error cases:

- Unsupported expression syntax.
- More than one expression in a single scalar string.
- Path does not start with `settings.`.
- Missing property in the parent settings object.
- Attempt to continue through a non-object value.

## Serialization Model

The current configuration model already treats settings as JSON-like data. This feature should continue to operate within those same value kinds:

- object
- array
- string
- number
- boolean
- null

The propagation feature should not introduce YAML-specific special cases beyond what already comes through configuration loading.

In particular:

- Exact replacement preserves the resolved JSON-like value kind.
- Embedded string replacement converts only the inserted fragment to text.
- String interpolation never mutates surrounding YAML structure.

## Examples

### Pass-through object

Parent convention invocation:

```yaml
path: ./conventions/parent
settings:
  formatter:
    include:
      - src/**/*.cs
      - tests/**/*.cs
    severity: warning
```

Child convention reference in `parent/convention.yml`:

```yaml
path: ../format-code
settings:
  formatter: ${{ settings.formatter }}
```

Resolved child effective settings:

```yaml
formatter:
  include:
    - src/**/*.cs
    - tests/**/*.cs
  severity: warning
```

### Nested string interpolation

Parent convention invocation:

```yaml
path: ./conventions/parent
settings:
  sdk:
    version: 10
```

Child convention reference in `parent/convention.yml`:

```yaml
path: ../update-readme
settings:
  heading: .NET SDK ${{ settings.sdk.version }}
```

Resolved child effective settings:

```yaml
heading: .NET SDK 10
```

### Chained propagation through nested composites

Top-level reference:

```yaml
path: ./conventions/outer
settings:
  sdk:
    version: 10
  package:
    id: RepoConventions
```

`outer/convention.yml`:

```yaml
conventions:
  - path: ../inner
    settings:
      version: ${{ settings.sdk.version }}
      packageId: ${{ settings.package.id }}
```

`inner/convention.yml`:

```yaml
conventions:
  - path: ../child
    settings:
      packageId: ${{ settings.packageId }}
      version: ${{ settings.version }}
```

Resolved effective settings seen by `child`:

```yaml
packageId: RepoConventions
version: 10
```

The inner composite resolves against its own effective settings:

```yaml
version: 10
packageId: RepoConventions
```

It does not resolve directly against the outer top-level shape.

## Deliberate Limitations For V1

The first version should stay narrow even if broader expression support is desirable later.

Deliberate limitations:

- Only the `settings` context exists.
- Only dotted property access is allowed.
- Only one expression may appear in a scalar string.
- No property-name interpolation.
- No interpolation in convention `path`.
- No array indexing.
- No escaping syntax for literal `${{ ... }}` text.

These constraints keep the feature predictable and reduce the risk of accidentally creating a partial expression language that becomes hard to evolve.

## Suggested Acceptance Criteria

- A child setting whose full scalar value is `${{ settings.foo }}` receives the parent value with its original type.
- A child setting string containing one `${{ settings.foo }}` expression produces a string result.
- Embedded non-string values are inserted as compact JSON.
- Arrays and objects in child settings are traversed recursively.
- Missing paths fail before any convention is applied.
- Nested composites resolve against the immediate parent's effective settings.
- Unsupported syntax produces a clear validation error instead of being ignored.
