---
name: bump-version
description: Bump the SDK version after publishing a release. Reads the current version from src/Directory.Build.props, suggests the next minor version, and creates a pull request with the change. Use when asked to bump the version, prepare for the next release, or increment the version number.
compatibility: Requires gh CLI with repo access for creating branches and pull requests.
---

# Bump Version

Bump the SDK version in `src/Directory.Build.props` after publishing a release and create a pull request with the change.

## Process

### Step 1: Read Current Version

Read `src/Directory.Build.props` on the default branch and extract:
- `<VersionPrefix>` — the `MAJOR.MINOR.PATCH` version
- `<VersionSuffix>` — the prerelease suffix (e.g. `preview.1`), if present

Display the current version to the user: `{VersionPrefix}-{VersionSuffix}` or `{VersionPrefix}` if no suffix.

### Step 2: Determine Next Version

If the user provided a target version in their prompt, use it. Otherwise, suggest the next **minor** version with the same suffix pattern:

- Current `0.9.0` with suffix `preview.1` → suggest `0.10.0-preview.1`
- Current `1.0.0` with no suffix → suggest `1.1.0`
- Current `1.2.3` with suffix `rc.1` → suggest `1.3.0-rc.1`

Present the suggestion and let the user confirm or provide an alternative. Parse the confirmed version into its `VersionPrefix` and `VersionSuffix` components.

### Step 3: Create Pull Request

1. Create a new branch named `bump-version-to-{version}` (e.g. `bump-version-to-0.10.0-preview.1`) from the default branch
2. Update `src/Directory.Build.props`:
   - Set `<VersionPrefix>` to the new prefix
   - Set `<VersionSuffix>` to the new suffix, or remove the element if there is no suffix
3. Commit with message: `Bump version to {version}`
4. Push the branch and create a pull request:
   - **Title**: `Bump version to {version}`
   - **Label**: `infrastructure`
   - **Base**: default branch

### Step 4: Confirm

Display the pull request URL to the user.
