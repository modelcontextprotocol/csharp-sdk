# Semantic Versioning Assessment Guide

This reference describes how to assess the appropriate [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) (SemVer) release level for the C# MCP SDK based on the changes queued since the previous release.

## SemVer Summary

The SDK follows SemVer 2.0.0 as documented in the [C# SDK Versioning](https://modelcontextprotocol.github.io/csharp-sdk/versioning.html) documentation. Given a version `MAJOR.MINOR.PATCH`:

- **MAJOR**: Increment when incompatible API changes are included
- **MINOR**: Increment when functionality is added in a backward-compatible manner
- **PATCH**: Increment when only backward-compatible bug fixes are included

When incrementing:
- MAJOR resets MINOR and PATCH to 0 (e.g., `1.2.3` → `2.0.0`)
- MINOR resets PATCH to 0 (e.g., `1.2.3` → `1.3.0`)
- PATCH increments only the PATCH component (e.g., `1.2.3` → `1.2.4`)

## Assessment Criteria

Evaluate every PR in the release range against these criteria, ordered from highest to lowest precedence.

### MAJOR — Incompatible API Changes

Recommend a MAJOR version increment if **any** of the following are present:

- Confirmed breaking changes from the breaking change audit (API or behavioral)
- Removal of public types, members, or interfaces
- Changes to parameter types, order, or count on public methods
- Return type changes on public methods or properties
- Sealing of previously unsealed types (with accessible constructors)
- Escalation of `[Obsolete]` from warning to error
- Removal of previously obsolete APIs

**Exception — Experimental APIs**: Changes to APIs annotated with `[Experimental]` do not require a MAJOR increment, even if they would otherwise be considered breaking. Note these changes but classify the release level based on the non-experimental changes.

### MINOR — Backward-Compatible New Functionality

Recommend a MINOR version increment if no MAJOR criteria are met but **any** of the following are present:

- New public types, methods, properties, or events
- New overloads or extension methods
- New configuration options or parameters with default values
- New MCP capabilities or protocol features
- Addition of `[Obsolete]` attributes producing build warnings (step 1 of obsoletion lifecycle)
- Addition of `[Experimental]` attributes on new APIs
- New interfaces implemented by existing types
- Performance improvements that introduce new API surface
- Changes to `[Experimental]` APIs (regardless of whether they would be breaking outside the experimental surface)

### PATCH — Backward-Compatible Bug Fixes Only

Recommend a PATCH version increment if no MAJOR or MINOR criteria are met. PATCH-level changes include:

- Bug fixes that correct incorrect behavior
- Spec compliance corrections
- Security fixes
- Performance improvements with no API surface changes
- Documentation changes (XML doc comments shipped in packages)
- Test-only changes (no impact on shipped packages)
- Infrastructure-only changes (CI, build system, dependencies)

**Note**: Releases that contain _only_ documentation, test, or infrastructure changes may not warrant a release at all. Flag this to the user if no shipped-package changes are present.

## Computing the Recommended Version

1. Parse the previous release tag to extract `MAJOR.MINOR.PATCH` and any pre-release suffix.
2. Apply the assessed level:
   - MAJOR: `(MAJOR+1).0.0`
   - MINOR: `MAJOR.(MINOR+1).0`
   - PATCH: `MAJOR.MINOR.(PATCH+1)`
3. If the previous release had a pre-release suffix, carry the same suffix pattern forward (e.g., `-preview.1`).

**Examples** from previous release `v1.2.0`:

| Level | Recommended |
|-------|-------------|
| PATCH | `v1.2.1` |
| MINOR | `v1.3.0` |
| MAJOR | `v2.0.0` |

**Examples** from previous release `v1.0.0-preview.1`:

| Level | Recommended |
|-------|-------------|
| PATCH | `v1.0.1-preview.1` |
| MINOR | `v1.1.0-preview.1` |
| MAJOR | `v2.0.0-preview.1` |

## Comparing Against the Candidate Version

After computing the recommended version:

1. Compare it against the candidate version (from `src/Directory.Build.props` or an existing draft release tag).
2. Present one of three outcomes:
   - **Match**: The candidate aligns with the assessment. Proceed with confidence.
   - **Under-versioned**: The candidate uses a lower increment level than the changes warrant (e.g., candidate is `1.2.1` but changes include new APIs requiring `1.3.0`). Flag this as a concern — the version should be corrected.
   - **Over-versioned**: The candidate uses a higher increment level than strictly required (e.g., candidate is `2.0.0` but no breaking changes). This is permitted by SemVer but worth noting for the user's awareness.

## Presentation Format

Present the assessment as a summary table followed by a rationale:

```
### Version Assessment

| Aspect | Finding |
|--------|---------|
| Previous release | v1.0.0 |
| Breaking changes | None confirmed |
| New API surface | Yes — 3 PRs add new public APIs |
| Bug fixes | Yes — 2 PRs fix runtime behavior |
| Recommended level | **MINOR** |
| Recommended version | `v1.1.0` |
| Candidate version | `1.1.0` ✅ matches |

**Rationale**: Three PRs introduce new public API surface (#101, #105, #112)
including new extension methods and configuration options. No confirmed breaking
changes. The candidate version in Directory.Build.props aligns with the MINOR
assessment.
```

When the candidate does not match, flag the discrepancy:

```
| Candidate version | `1.0.1` ⚠️ under-versioned (PATCH < MINOR) |
```
