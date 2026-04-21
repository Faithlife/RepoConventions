# Composite Convention Settings Propagation

## Status

Implemented.

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

Notes:

- The path is split on dots. A property whose literal name contains a dot cannot be addressed in this first version.
- The path syntax is intentionally smaller than GitHub Actions. The goal is familiarity, not compatibility with the full expression language.
- A string scalar may contain zero, one, or multiple supported expressions.

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
- If an exact-value array replacement appears as one item of an array, the resolved array's items are expanded into the containing array rather than producing a nested array.

This allows propagation to appear anywhere inside the child settings value tree, including inside nested objects and arrays.

## Exact-Value Replacement

If a child setting value is a string whose entire contents are exactly one supported expression, the resolved value replaces that string as its original data type.

If the referenced path is missing and the exact-value replacement is used as an object property value, that property is omitted from the resolved settings object.

If the referenced path is missing and the exact-value replacement is used as an array item, that array item is omitted from the resolved settings array.

If the resolved value is an array and the exact-value replacement is used as an array item, the array is spliced into the containing array.

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

Array splicing example:

Parent settings:

```yaml
labels:
  - automation
  - conventions
```

Child settings template:

```yaml
items:
  - before
  - ${{ settings.labels }}
  - after
```

Resolved child settings:

```yaml
items:
  - before
  - automation
  - conventions
  - after
```

## Embedded String Replacement

If a child setting value is a string that contains one or more supported expressions along with other text, the overall result stays a string.

Replacement behavior:

- Each supported expression in the string is resolved independently.
- If the resolved value is a string, insert the raw string value.
- If the referenced path is missing, insert the empty string.
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
combined: ${{ settings.name }} v${{ settings.version }}
missingMessage: hello-${{ settings.missing }}-world
```

Resolved child settings:

```yaml
message: Running repo-conventions
versionTag: v10
enabledMessage: enabled=true
labelsMessage: labels=["automation","conventions"]
metadataMessage: metadata={"owner":"Faithlife"}
nullMessage: nullable=null
combined: repo-conventions v10
missingMessage: hello--world
```

For non-string values, the JSON text should be compact and deterministic rather than pretty-printed.

## Missing Values And Type Mismatches

A referenced path may be missing.

Missing-value behavior depends on the context in which the expression is used.

- If an expression is used inside a larger string value, a missing path resolves to the empty string.
- If an expression is used as an exact object property value, the property is omitted.
- If an expression is used as an exact array item, the array item is omitted.

Type mismatches still fail.

Resolution fails if:

- An intermediate path segment resolves to a non-object before the final segment is reached.

Examples:

- `${{ settings.sdk.version }}` used in `SDK ${{ settings.sdk.version }}` resolves to the string `SDK` followed by a trailing space if `settings.sdk` is missing.
- `version: ${{ settings.sdk.version }}` omits `version` if `settings.sdk` is missing.
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
- Path does not start with `settings.`.
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

### Multiple expressions in one string

Parent convention invocation:

```yaml
path: ./conventions/parent
settings:
  owner: Faithlife
  repo: RepoConventions
  version: 10
```

Child convention reference in `parent/convention.yml`:

```yaml
path: ../child
settings:
  displayName: ${{ settings.owner }}/${{ settings.repo }}@v${{ settings.version }}
```

Resolved child effective settings:

```yaml
displayName: Faithlife/RepoConventions@v10
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
- No property-name interpolation.
- No interpolation in convention `path`.
- No array indexing.
- No escaping syntax for literal `${{ ... }}` text.

These constraints keep the feature predictable and reduce the risk of accidentally creating a partial expression language that becomes hard to evolve.

## Implementation Considerations

The substitution should be defined over the parsed settings value tree, not over raw YAML text.

That means a YAML parser can and should be used before substitution.

Reasons:

- Exact-value replacement needs container-aware behavior such as omitting an object property.
- Exact-value replacement inside arrays needs container-aware splicing behavior.
- Embedded string replacement is still scalar-oriented after parsing, because the parser preserves the original scalar text for string values.
- Parsing first avoids inventing text-level rules for indentation, quoting, flow collections, or block scalars.

The current configuration model already converts YAML into a JSON-like object tree. This feature fits naturally into that model by walking the parsed tree and producing a new parsed tree.

The main implementation challenges are:

- Distinguishing exact-expression scalars from string scalars that merely contain one or more expressions.
- Carrying parent context so missing exact-value expressions can omit object properties or array items rather than yielding placeholder values.
- Supporting array splicing only for exact-expression array items, while preserving nested arrays in every other case.
- Treating missing paths differently from type mismatches.
- Producing deterministic JSON text when non-string values are embedded into strings.
- Preserving the current configuration-loading behavior and not introducing YAML-only special cases that cannot be represented in the JSON-like settings model.

Because substitution is applied after YAML parsing, this design does not require defining a special YAML subset just for interpolation.

The practical support boundary remains the YAML that the existing parser accepts and that the configuration loader can represent as JSON-like settings values. If future requirements need interpolation in keys, anchors, tags, or other syntax-level YAML features, that would require either tighter YAML support rules or a richer representation than the current parsed value tree.

## Suggested Acceptance Criteria

- A child setting whose full scalar value is `${{ settings.foo }}` receives the parent value with its original type.
- A child setting string containing one or more `${{ settings.foo }}` expressions produces a string result.
- Embedded non-string values are inserted as compact JSON.
- Missing values inside strings resolve to the empty string.
- Missing exact-value object properties are omitted.
- Missing exact-value array items are omitted.
- Arrays and objects in child settings are traversed recursively.
- Exact-value arrays used as array items are spliced into the containing array.
- Nested composites resolve against the immediate parent's effective settings.
- Unsupported syntax produces a clear validation error instead of being ignored.
- Type mismatches fail before any convention is applied.
