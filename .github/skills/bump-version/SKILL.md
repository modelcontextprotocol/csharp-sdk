---
name: bump-version
description: Assess and bump the SDK version using Semantic Versioning 2.0.0. Evaluates queued changes to recommend PATCH/MINOR/MAJOR, updates src/Directory.Build.props, and creates a pull request. Owns the SemVer assessment logic shared by prepare-release and publish-release. Use when asked to bump the version, assess the version, or determine what the next version should be.
compatibility: Requires gh CLI with repo access for creating branches and pull requests. GitHub API access for PR details when performing SemVer-informed assessment.
---

# Bump Version

Assess and bump the SDK version in `src/Directory.Build.props` to prepare for the next release. This skill owns the [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) assessment logic — the [SemVer assessment guide](references/semver-assessment.md) is the single source of truth for version assessment criteria used across the release workflow by both the **prepare-release** and **publish-release** skills.

> **Note**: For comprehensive release preparation — including ApiCompat/ApiDiff, documentation review, and release notes — use the **prepare-release** skill, which incorporates version assessment as part of its broader workflow.

## Process

### Step 1: Read Current Version and Previous Release

Read `src/Directory.Build.props` on the default branch and extract:
- `<VersionPrefix>` — the `MAJOR.MINOR.PATCH` version
- `<VersionSuffix>` — the prerelease suffix (e.g. `preview.1`), if present

Display the current version to the user: `{VersionPrefix}-{VersionSuffix}` or `{VersionPrefix}` if no suffix.

Determine the previous release tag from `gh release list` (most recent **published** release). Draft releases must be ignored — they represent a pending release that has not yet shipped. Use `--exclude-drafts` or filter to only published releases when querying.

### Step 2: Assess and Determine Next Version

If the user provided a target version in their prompt, use it directly. Otherwise, determine the next version using one of two approaches:

#### SemVer-Informed Assessment (Preferred)

When context about queued changes is available or can be gathered, assess the version following the [SemVer assessment guide](references/semver-assessment.md):

1. Get the list of PRs merged between the previous release tag and the target commit (typically HEAD).
2. Classify the release level:
   - **MAJOR** — if any confirmed breaking changes (API or behavioral), excluding `[Experimental]` APIs
   - **MINOR** — if new public API surface, features, or obsoletion warnings are present
   - **PATCH** — if only backward-compatible bug fixes, documentation, tests, or infrastructure changes
3. Compute the recommended version from the previous release tag (see the assessment guide for increment rules and pre-release suffix handling).
4. Compare against the current version in `Directory.Build.props` and flag any discrepancy.
5. Present the assessment with a summary table and rationale, then get user confirmation.

#### Default Suggestion (Fallback)

When a quick bump is needed without full change analysis, suggest the next **minor** version with the same suffix pattern:

- Current `0.9.0` with suffix `preview.1` → suggest `0.10.0-preview.1`
- Current `1.0.0` with no suffix → suggest `1.1.0`
- Current `1.2.3` with suffix `rc.1` → suggest `1.3.0-rc.1`

Present the suggestion and let the user confirm or provide an alternative.

In either case, parse the confirmed version into its `VersionPrefix` and `VersionSuffix` components.

### Step 3: Create Pull Request

1. Create a new branch named `bump-version-to-{version}` (e.g. `bump-version-to-0.10.0-preview.1`) from the default branch
2. Update `src/Directory.Build.props`:
   - Set `<VersionPrefix>` to the new prefix
   - Set `<VersionSuffix>` to the new suffix, or remove the element if there is no suffix
   - Update `<PackageValidationBaselineVersion>` if the MAJOR version has changed
3. Commit with message: `Bump version to {version}`
4. Push the branch and create a pull request:
   - **Title**: `Bump version to {version}`
   - **Label**: `infrastructure`
   - **Base**: default branch

### Step 4: Confirm

Display the pull request URL to the user.
