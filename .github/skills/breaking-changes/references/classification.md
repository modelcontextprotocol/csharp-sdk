# Breaking Change Classification Guide

This guide defines how to identify and classify breaking changes in the C# MCP SDK. It is derived from the [dotnet/runtime breaking change guidelines](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/breaking-changes.md).

## Two Categories of Breaking Changes

### API Breaking Changes (Compile-Time)
Changes that alter the public API surface in ways that break existing code at compile time:

- **Renaming or removing** a public type, member, or parameter
- **Changing the return type** of a method or property
- **Changing parameter types, order, or count** on a public method
- **Sealing** a type that was previously unsealed (when it has accessible constructors)
- **Making a virtual member abstract**
- **Adding `abstract` to a member** when the type has accessible constructors and is not sealed
- **Removing an interface** from a type's implementation
- **Changing the value** of a public constant or enum member
- **Changing the underlying type** of an enum
- **Adding `readonly` to a field**
- **Removing `params` from a parameter**
- **Adding/removing `in`, `out`, or `ref`** parameter modifiers
- **Renaming a parameter** (breaks named arguments and late-binding)
- **Adding the `[Obsolete]` attribute** or changing its diagnostic ID
- **Adding the `[Experimental]` attribute** or changing its diagnostic ID
- **Removing accessibility** (making a public/protected member less visible)

### Behavioral Breaking Changes (Runtime)
Changes that don't break compilation but alter observable behavior:

- **Throwing a new/different exception type** in an existing scenario (unless it's a more derived type)
- **No longer throwing an exception** that was previously thrown
- **Changing return values** for existing inputs
- **Decreasing the range of accepted values** for a parameter
- **Changing default values** for properties, fields, or parameters
- **Changing the order of events** being fired
- **Removing the raising of an event**
- **Changing timing/order** of operations
- **Changing parsing behavior** and throwing new errors
- **Changing serialization format** or adding new fields to serialized types

## Classification Buckets

### Bucket 1: Clear Public Contract Violation
Obvious breaking changes to the public API shape. **Always flag these.**

### Bucket 2: Reasonable Grey Area
Behavioral changes that customers would have reasonably depended on. **Flag and discuss with user.**

### Bucket 3: Unlikely Grey Area
Behavioral changes that customers could have depended on but probably wouldn't (e.g., corner case corrections). **Flag with lower confidence.**

### Bucket 4: Clearly Non-Public
Changes to internal surface or behavior (e.g., internal APIs, private reflection). **Generally not flagged** unless they could affect ecosystem tools.

## What to Study for Each PR

For every PR in the range, examine:

1. **PR description** — Authors often describe breaking changes here
2. **Linked issues** — May contain discussion about breaking impact
3. **Review comments** — Reviewers may have flagged breaking concerns
4. **Code diff** — Look at changes to:
   - Public type/member signatures
   - Exception throwing patterns
   - Default values and constants
   - Return value changes
   - Parameter validation changes
   - Attribute changes (`[Obsolete]`, `[Experimental]`, etc.)
5. **Labels** — Check if `breaking-change` is already applied
