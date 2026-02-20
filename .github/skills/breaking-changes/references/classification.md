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

### Bug Fixes (Exclude)
Changes that correct incorrect behavior, fix spec compliance, or address security issues are **not breaking changes** even if they alter observable behavior. Examples:
- Fixing encoding to match a specification requirement
- Correcting a logger category or metric name that was wrong
- Fixing exception message leaks that were a security concern
- Moving data to the correct location per protocol spec evolution
- Setting a flag that should have been set automatically (e.g., `IsError` for error content)
- Returning a more specific/informative exception for better diagnostics

If a change is primarily a bug fix or spec compliance correction, exclude it from the breaking changes list even though the observable behavior changes.

### Bucket 4: Clearly Non-Public
Changes to internal surface or behavior (e.g., internal APIs, private reflection). **Generally not flagged** unless they could affect ecosystem tools.

## SDK Versioning Policy

The classification rules above are derived from the dotnet/runtime breaking change guidelines, but the MCP SDK has its own versioning policy (see `docs/versioning.md`) that provides additional context for classification decisions.

### Pre-1.0 Preview Status

Prior to a stable 1.0.0 release, the SDK is in preview and breaking changes can be introduced without prior notice. This does **not** change how breaks are classified — they should still be flagged, labeled, and documented — but it affects the **severity assessment**. Preview consumers expect breaks, so migration guidance matters more than avoidance.

### Experimental APIs

APIs annotated with `[Experimental]` (using `MCP`-prefixed diagnostic codes) can change at any time, including within PATCH or MINOR updates. Changes to experimental APIs should still be **noted** in the audit, but classified as **Bucket 3 (Unlikely Grey Area)** or lower unless the API has been widely adopted despite its experimental status.

### Obsoletion Lifecycle

The SDK follows a three-step obsoletion process:

1. **MINOR update**: API marked `[Obsolete]` producing _build warnings_ with migration guidance
2. **MAJOR update**: API marked `[Obsolete]` producing _build errors_ (API throws at runtime)
3. **MAJOR update**: API removed entirely (expected to be rare)

When auditing, classify each step appropriately:
- Step 1 (adding `[Obsolete]` warning) → API breaking change (new build warning)
- Step 2 (escalating to error) → API breaking change (previously working code now fails)
- Step 3 (removal) → API breaking change; migration guidance should note prior deprecation

In exceptional circumstances — especially during the pre-1.0 preview period — the obsoletion lifecycle may be compressed (e.g., marking obsolete and removing in the same MINOR release). This should still be flagged as a breaking change but the migration guidance should explain the rationale.

### Spec-Driven Changes

Breaking changes necessitated by MCP specification evolution should be flagged and documented normally, but the migration guidance should reference the spec change. If a spec change forces an incompatible API change, preference is given to supporting the most recent spec version.

## Compatibility Switches

When a breaking change includes an `AppContext` switch or other opt-in/opt-out mechanism, always note it in the migration guidance. Search for `AppContext.TryGetSwitch`, `DOTNET_` environment variables, and similar compat patterns in the diff. Include the switch name and the value that alters the behavior:

```
* Compat switch: `ModelContextProtocol.AspNetCore.AllowNewSessionForNonInitializeRequests` = `true` restores previous behavior
```

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
   - `AppContext.TryGetSwitch` or environment variable compat switches
5. **Labels** — Check if `breaking-change` is already applied
