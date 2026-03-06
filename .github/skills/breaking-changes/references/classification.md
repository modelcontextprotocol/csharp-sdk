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

### Experimental APIs

APIs annotated with `[Experimental]` (using `MCP`-prefixed diagnostic codes) can change at any time, including within PATCH or MINOR updates. Changes to experimental APIs should still be **noted** in the audit, but classified as **Bucket 3 (Unlikely Grey Area)** or lower unless the API has been widely adopted despite its experimental status.

#### APIs exclusively reachable through `[Experimental]` gates

A change to a non-experimental public API is **not considered breaking** if it only affects consumers who have already opted into an `[Experimental]` code path. The key question is: *can a consumer reach the breaking impact without suppressing an experimental diagnostic?*

For example, adding an abstract member to a public abstract class is normally a Bucket 1 break (anyone deriving from the class must implement the new member). However, if the class's only accessible constructor is marked `[Experimental]`, then deriving from it already requires suppressing the experimental diagnostic — meaning the consumer has explicitly accepted that the API is subject to change.

**How to identify this pattern:**
1. A change would normally be classified as a breaking change (e.g., CP0005 — adding abstract member to abstract type)
2. Trace the code path a consumer must follow to be affected by the break
3. If **every** such path requires the consumer to use an `[Experimental]`-annotated API (constructor, method, type, etc.), the break is dismissed

### Obsoletion Lifecycle

The SDK follows a three-step obsoletion process:

1. **MINOR update**: API marked `[Obsolete]` producing _build warnings_ with migration guidance
2. **MAJOR update**: API marked `[Obsolete]` producing _build errors_ (API throws at runtime)
3. **MAJOR update**: API removed entirely (expected to be rare)

When auditing, classify each step appropriately:
- Step 1 (adding `[Obsolete]` warning) → API breaking change (new build warning)
- Step 2 (escalating to error) → API breaking change (previously working code now fails)
- Step 3 (removal) → API breaking change; migration guidance should note prior deprecation

In exceptional circumstances, the obsoletion lifecycle may be compressed (e.g., marking obsolete and removing in the same MINOR release). This should still be flagged as a breaking change but the migration guidance should explain the rationale.

### Spec-Driven Changes

Breaking changes necessitated by MCP specification evolution should be flagged and documented normally, but the migration guidance should reference the spec change. If a spec change forces an incompatible API change, preference is given to supporting the most recent spec version.

## Compatibility Switches

When a breaking change includes an `AppContext` switch or other opt-in/opt-out mechanism, always note it in the migration guidance. Search for `AppContext.TryGetSwitch`, `DOTNET_` environment variables, and similar compat patterns in the diff. Include the switch name and the value that alters the behavior:

```
* Compat switch: `ModelContextProtocol.AspNetCore.AllowNewSessionForNonInitializeRequests` = `true` restores previous behavior
```

## Dismissing Potential Breaking Changes

When a change appears to be breaking but is dismissed (e.g., as a bug fix, clearly non-public, or exclusively gated by `[Experimental]` APIs), the audit must present the full rationale to the user for verification.

### Gathering Supporting Evidence

Before dismissing a potential break, review the PR description and all PR comments (both review comments and general comments) for discussion about the breaking change. Authors and reviewers often explain *why* a change is acceptable — for example, noting that the affected type is gated by an experimental constructor, that the previous behavior was incorrect per the spec, that no external consumers exist yet, or that compatibility suppressions were added intentionally. This discussion serves as supporting evidence for the dismissal and should be cited in the audit findings.

### Presenting Dismissals

Every dismissed potential break must be reported to the user with enough detail for them to verify the conclusion. The audit must:

1. **Identify what would normally be breaking and why** (e.g., "CP0005 — adding abstract member `Completion` to abstract class `McpClient`")
2. **Explain the specific reason for dismissal** (e.g., "Bug fix correcting incorrect behavior per the MCP spec" or "`McpClient`'s only accessible constructor is `protected` and marked `[Experimental(MCPEXP002)]` with message 'Subclassing McpClient and McpServer is experimental and subject to change.'")
3. **Cite any supporting discussion** from the PR description or comments (e.g., "Reviewers discussed the addition and did not flag it as a breaking concern; compatibility suppressions were added for CP0005")
4. **Conclude with the dismissal and its category** (e.g., "Dismissed — bug fix correcting spec-non-compliant behavior" or "Dismissed — exclusively gated by `[Experimental]` API. Do not apply the `breaking-change` label.")

This transparency allows the user to verify each dismissal rationale and override it if the justification is insufficient.

## What to Study for Each PR

For every PR in the range, examine:

1. **PR description** — Authors often describe breaking changes here, or explain why a potentially breaking change is acceptable
2. **Linked issues** — May contain discussion about breaking impact
3. **Review comments** — Reviewers may have flagged breaking concerns or discussed why a change is acceptable despite appearing breaking (e.g., experimental gates, no external consumers, compatibility suppressions). These discussions are critical evidence when dismissing potential breaks.
4. **General comments** — Authors and reviewers sometimes discuss breaking change justification in the PR conversation thread rather than in review comments
5. **Code diff** — Look at changes to:
   - Public type/member signatures
   - Exception throwing patterns
   - Default values and constants
   - Return value changes
   - Parameter validation changes
   - Attribute changes (`[Obsolete]`, `[Experimental]`, etc.)
   - `AppContext.TryGetSwitch` or environment variable compat switches
   - Compatibility suppressions (e.g., `CompatibilitySuppressions.xml` for ApiCompat CP0005 etc.)
6. **Labels** — Check if `breaking-change` is already applied
