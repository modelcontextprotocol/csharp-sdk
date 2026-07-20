---
name: prepare-release
description: Prepare a new release for the C# MCP SDK. Assesses Semantic Versioning level (PATCH/MINOR/MAJOR), bumps the version, runs ApiCompat and ApiDiff, reviews documentation, updates changelogs, drafts release notes, and creates a pull request with all release artifacts. Use when asked to prepare a release, start a release, create a release PR, or assess what the next release should be.
compatibility: Requires gh CLI with repo access, GitHub API access for PR details and timeline events, dotnet CLI for building and packing, and git for branch management.
---

# Prepare Release

Prepare a new release for the `modelcontextprotocol/csharp-sdk` repository. This skill assesses the appropriate [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) level based on queued changes, bumps the version, runs API compatibility and diff tools, reviews documentation, drafts release notes, and creates a pull request containing all release artifacts.

> **Safety: This skill creates a local branch and PR. It must never create a GitHub release.** Release creation is handled by the **publish-release** skill after this PR is merged.

> **User confirmation required: This skill NEVER pushes a branch or creates a pull request without explicit user confirmation.** The user must review and approve all details before any remote operations occur.

Use the shared [release branch reference](../shared-resources/release-branches.md) for branch roles, previous-release lookup rules, and release work-branch naming.

## Process

Work through each step sequentially. Present findings at each step and get user confirmation before proceeding. Skip any step that has no applicable items.

### Step 1: Select Source Branch

List candidate source/base branches via:
`gh api repos/{owner}/{repo}/branches --paginate --jq '[.[] | select(.name == "main" or (.name | startswith("release/"))) | .name]'`

Present the list to the user and ask them to choose the source/base branch. Default selection: `main`.

The selected branch drives every subsequent step:
1. The branch on which the candidate version is read from `src/Directory.Build.props`.
2. The "previous release" lookup (constrained to `v{MAJOR}.*` on `release/{MAJOR}.x`).
3. The commit range from which PRs are collected.
4. The PR base (`--base`) for `gh pr create` at the end of the skill.

See [release-branches.md](../shared-resources/release-branches.md) for the structured branch rules.

### Step 2: Determine Target and Gather PRs

The user may provide:
- **A git ref** (commit SHA, branch, or tag) — use as the target commit relative to the selected source/base branch
- **No context** — show the last 5 commits on the selected source/base branch (noting HEAD) and offer the option to enter a branch or tag name instead

Once the target is established:
1. Determine the previous release tag from `gh release list` (most recent **published** release — exclude drafts with `--exclude-drafts`). Use the selected source/base branch context: on `release/{MAJOR}.x`, restrict candidates to tags matching `v{MAJOR}.*`; on `main`, use the most recent published release globally.
2. Get the full list of PRs merged between the previous release tag and the target commit on the selected branch.
3. Read `src/Directory.Build.props` **at the target commit**. Extract `<VersionPrefix>` and `<VersionSuffix>`; the **candidate version** is `{VersionPrefix}` plus `-{VersionSuffix}` when the suffix is present (for example, `2.0.0-preview.1`).

### Step 3: Categorize and Attribute

Sort every PR into one of four categories. See [references/categorization.md](references/categorization.md) for detailed guidance.

| Category | Content |
|----------|---------|
| **What's Changed** | Features, bug fixes, improvements, breaking changes |
| **Documentation Updates** | PRs whose sole purpose is documentation |
| **Test Improvements** | Adding, fixing, or unskipping tests; flaky test repairs |
| **Repository Infrastructure Updates** | CI/CD, dependency bumps, version bumps, build system |

**Entry format** — `* Description #PR by @author` with co-authors when present:
```
* Description #PR by @author
* Description #PR by @author (co-authored by @user1 @Copilot)
```

**Attribution rules:**
- Harvest `Co-authored-by` trailers from **all commits** in each PR (not just the merge commit) to identify co-authors. Do this for every PR regardless of primary author.
- For Copilot-authored PRs, additionally check the `copilot_work_started` timeline event to identify the triggering user. That person becomes the primary author; `@Copilot` becomes a co-author.
- Omit the co-author parenthetical when there are none
- Sort entries within each section by merge date (chronological)

### Step 4: Breaking Change Audit

Invoke the **breaking-changes** skill with the commit range from the previous release tag to the target commit. Examine every PR, assess impact, reconcile labels (offering to add/remove labels and comment on PRs), and get user confirmation.

Use the results (confirmed breaking changes with impact ordering and detail bullets) in the remaining steps.

### Step 5: Assess Release Version

Using the categorized PRs from Step 3 and confirmed breaking changes from Step 4, assess the appropriate [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) release level. Follow the [SemVer assessment guide](../bump-version/references/semver-assessment.md) (owned by the **bump-version** skill) for the full assessment criteria.

1. **Classify the release level**:
   - **MAJOR** — if any confirmed breaking changes are present (API or behavioral), excluding changes to `[Experimental]` APIs
   - **MINOR** — if no breaking changes but new public APIs, features, or obsoletion warnings are introduced
   - **PATCH** — otherwise
2. **Compute the recommended version** from the previous release tag and branch context:
   - Increment the appropriate component (MAJOR resets MINOR.PATCH to 0; MINOR resets PATCH to 0)
   - For prerelease candidates such as `preview.N` or `rc.N`, the recommendation may simply increment the trailing integer per the assessment guide
3. **Compare against the candidate version** from `src/Directory.Build.props`. Flag any discrepancy:
   - **Under-versioned**: The candidate is lower than the recommended level. This is a concern that should be resolved.
   - **Over-versioned**: The candidate is higher than strictly required. This is acceptable under SemVer but worth noting.
4. **Present the assessment** with a summary table showing the previous release, change classification, recommended level, recommended version, and any discrepancy with the candidate. Include a brief rationale citing the most significant PRs.
5. **Get user confirmation** of the release version before proceeding.

### Step 6: Create Release Branch and Bump Version

After the version is confirmed:

1. Create a local branch named `release-{version}` from the target commit (e.g., `release-2.0.0-preview.1`, `release-1.3.1`).
2. Update `src/Directory.Build.props`:
   - Set `<VersionPrefix>` to the confirmed stable component
   - Set `<VersionSuffix>` for prerelease versions, or clear it for stable versions; add the element if it is missing
   - Update `<PackageValidationBaselineVersion>` when appropriate. For the `2.0.0-preview` series, baseline is `1.3.0` (latest shipped 1.x). For subsequent stable releases, baseline is the previous shipped version of the same MAJOR or the latest stable from the previous MAJOR.
3. Build the solution to verify the version change compiles: `dotnet build`

This step creates local changes only — nothing is committed or pushed yet.

### Step 7: Run API Compatibility Check

Run API compatibility validation against the baseline version. Follow [references/apicompat-apidiff.md](references/apicompat-apidiff.md) for the full procedure.

1. Run `dotnet pack` to trigger package validation against `PackageValidationBaselineVersion`
2. Capture the ApiCompat output (compatibility issues, warnings, suppressions)
3. If there are unexpected compatibility breaks:
   - Cross-reference with the breaking change audit from Step 4
   - Present any unaccounted breaks to the user
   - If breaks are intentional, add appropriate entries to `CompatibilitySuppressions.xml` in the affected project directory
4. Record the ApiCompat results for inclusion in the PR description

### Step 8: Generate API Diff Report

Generate a human-readable diff of the public API surface between the previous release and the new version. Follow [references/apicompat-apidiff.md](references/apicompat-apidiff.md) for the full procedure, including how to install the `Microsoft.DotNet.ApiDiff.Tool` from the .NET transport feed.

1. Install `Microsoft.DotNet.ApiDiff.Tool` from the transport feed if not already installed (requires `--prerelease` and `--add-source` pointing to `https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet{MAJOR}-transport/nuget/v3/index.json`)
2. Download the baseline packages and build the current version in Release configuration
3. Run `dotnet apidiff` comparing baseline vs. current assemblies for each SDK package
4. Format the diff as markdown for inclusion in the PR description

> **If the ApiDiff tool cannot be installed or fails to produce output, STOP and inform the user.** Present the error and ask how to proceed. Do not fall back to a manual summary — the user must decide whether to troubleshoot, skip the API diff, or abort.

### Step 9: Review and Update Documentation

Review repository documentation for changes needed to compensate for or adapt to this release:

1. **NuGet package READMEs** -- Run the README content checklist from [references/readme-content.md](references/readme-content.md) and validate code samples:
   a. **Content checklist** -- Open `src/PACKAGE.md` and verify each item in the checklist:
      - **Package-list closure**: every shipping SDK package is listed. If a new package was introduced in this release, add it now. Use non-counting phrasing -- do not say "N main packages".
      - **Badge strategy**: all package badges use `nuget/vpre` for a prerelease series or `nuget/v` for a stable release. Switch all badges together if the release type has changed.
      - **Release-notes link**: add or update the link to `https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v{version}` for the confirmed release version. The tag does not yet exist at prepare time; the link is forward-referencing and resolves when the GitHub release is published.
      - **Root README.md sync**: mirror any package-list closure changes in the root `README.md`.
      - **Other salient content**: descriptions, getting-started links, version-specific notes.
   b. **Snippet validation** -- Validate that `csharp`-fenced code blocks in `src/PACKAGE.md` and `README.md` compile against the current SDK. Follow [references/readme-snippets.md](references/readme-snippets.md) for the full procedure. Propose fixes for any API mismatches.
2. **Conceptual documentation** -- Review `docs/` for content affected by the changes in this release. Update references to changed APIs, new features, or removed functionality.
3. **Versioning documentation** -- If the release introduces new versioning-relevant policies (new experimental APIs, obsoletion changes), verify `docs/versioning.md` reflects them.
4. **Changelogs** -- If the repository contains changelog files (e.g., `CHANGELOG.md`), update them with the release information. If no changelogs exist, skip this sub-step and note it in the summary.

Stage all documentation changes for inclusion in the release commit.

**Edge Cases for README updates:**
- **New package introduced** -- Add it to the package-list closure in `src/PACKAGE.md` and `README.md`. Use the package's `<Description>` from its `.csproj` as the short description.
- **Release type changes (prerelease to stable or vice versa)** -- Switch all package badges between `nuget/vpre` and `nuget/v` together.
- **Release tag does not yet exist at prepare time** -- The release-notes link is forward-referencing; it is verified to resolve during the publish-release step.

### Step 10: Draft Release Notes

Compose the release notes that will appear in the PR description and serve as the foundation for the **publish-release** skill. This is a draft — the final release notes will be refreshed when the GitHub release is created.

1. **Preamble** — Draft a short paragraph summarizing the release theme. Present it to the user for review and editing. The preamble is **required**.
2. **Breaking Changes** — sorted most → least impactful (from Step 4 results). Include the versioning docs link.
3. **What's Changed** — chronological; includes breaking change PRs
4. **Documentation Updates** — chronological
5. **Test Improvements** — chronological
6. **Repository Infrastructure Updates** — chronological
7. **Acknowledgements**:
   - New contributors (first contribution in this release)
   - Issue reporters (cite resolving PRs)
   - PR reviewers (single bullet, sorted by review count, no count shown)
8. **Full Changelog** link using the exact tag, including any suffix (for example, `v1.3.1` or `v2.0.0-preview.1`)

Omit empty sections. Present each section for user review before proceeding. Tag references in templates use `v{version}` exactly, including prerelease suffixes; the Full Changelog link compares the previous tag to the suffixed tag when applicable.

### Step 11: Commit Changes

Commit all changes to the `release-{version}` branch:

1. Stage all modified files (version bump, compatibility suppressions, documentation updates, changelog updates)
2. Commit with message: `Prepare release v{version}`
3. Do **not** push yet

### Step 12: Present Release Summary

Present **all** of the following details to the user for review. The user must confirm every aspect before proceeding to Step 13.

1. **Version number** with brief rationale for why this SemVer level was selected
2. **Source/base branch** selected in Step 1
3. **Branch name** (e.g., `release-2.0.0-preview.1`, `release-1.3.1`)
4. **Remote** the branch would be pushed to (show the configured remote, typically `origin`)
5. **Files changed** — list every file modified in the commit with a one-line summary of what changed in each:
   ```
   src/Directory.Build.props — Version bumped from 2.0.0-preview.1 to 2.0.0-preview.2
   src/ModelContextProtocol.Core/CompatibilitySuppressions.xml — Added 2 new suppressions
   README.md — Updated code sample for new API
   docs/experimental.md — Added new experimental API reference
   ```
6. **Draft release notes** — the complete release notes from Step 10
7. **API Compatibility results** — the ApiCompat output from Step 7
8. **API Diff report** — the API diff from Step 8
9. **Proposed PR title** (e.g., `Release v2.0.0-preview.1`, `Release v1.3.1`)
10. **Proposed PR description** — the assembled content combining release notes, ApiCompat, and ApiDiff

After presenting all details, explicitly ask the user:
> Would you like to push the branch and create the pull request?

**Do not proceed without explicit "yes" confirmation.**

### Step 13: Push Branch and Create Pull Request

Only after explicit user confirmation in Step 12:

1. Push the `release-{version}` branch to the remote
2. Create a pull request with `gh pr create --base {step-1-branch}`:
   - **Title**: `Release v{version}`
   - **Base**: the source/base branch selected in Step 1
   - **Head**: `release-{version}`
   - **Description**: The assembled PR description (see PR Description Template below)
   - **Labels**: Apply appropriate labels (e.g., `release`)
3. Present the PR URL to the user

**Important**: No draft GitHub release is created at this point. The **publish-release** skill handles release creation after this PR is merged.

## Edge Cases

- **PR spans categories**: categorize by primary intent
- **Copilot timeline missing**: fall back to `Co-authored-by` trailers to determine whether `@Copilot` should be a co-author; if still unclear, use `@Copilot` as primary author
- **No breaking changes**: omit the Breaking Changes section from release notes entirely
- **Single breaking change**: use the same numbered format as multiple
- **No user-facing changes**: if all PRs are documentation, tests, or infrastructure, flag that a release may not be warranted and ask the user whether to proceed
- **Version discrepancy**: if the candidate version from `Directory.Build.props` doesn't match the SemVer assessment, present the discrepancy and let the user decide the final version
- **Proposed MAJOR does not match branch MAJOR**: if the proposed version's MAJOR doesn't match the branch's MAJOR (for example, proposing `2.0.0-preview.2` on `release/1.x`), flag this as a warning and ask the user to confirm. Do not hard-fail. This is informational, not a policy enforcement.
- **Prerelease bump**: when the candidate version has a suffix like `preview.N`, the SemVer assessment may simply increment `N` rather than computing MAJOR/MINOR/PATCH. Refer to the SemVer assessment guide's Prereleases section.
- **No previous release**: if this is the first release, there is no previous tag; gather all PRs merged to the target
- **ApiCompat tooling unavailable**: fall back to `dotnet pack` output; note in the PR description that full ApiCompat was run via package validation only
- **API diff tool installation fails**: do not fall back to a manual summary; pause and present the installation error to the user, offering options to troubleshoot, skip the API diff section, or abort the release preparation
- **No changelogs in repo**: skip changelog updates; note in the summary
- **Branch already exists**: if `release-{version}` already exists locally or remotely, ask the user whether to reuse it, delete and recreate, or choose a different name
- **PackageValidationBaselineVersion update**: for the `2.0.0-preview` series, use `1.3.0`; for subsequent stable releases, use the previous shipped version of the same MAJOR or the latest stable from the previous MAJOR
- **CompatibilitySuppressions.xml**: when intentional breaks are found, add suppression entries and include the file in the commit; existing suppressions should be preserved
- **User declines PR creation**: if the user declines at Step 12, leave the local branch intact so they can review, modify, or push manually

## PR Description Template

The PR description combines release notes, ApiCompat, and ApiDiff into a single document. Omit empty sections. The `{version}` placeholder is the full version and may include a prerelease suffix (for example, `Release v2.0.0-preview.1`).

```markdown
# Release v{version}

[Preamble — summarize the release theme]

## Release Notes

### Breaking Changes

Refer to the [C# SDK Versioning](https://csharp.sdk.modelcontextprotocol.io/versioning.html) documentation for details on versioning and breaking change policies.

1. **Description #PR**
   * Detail of the break
   * Migration guidance

### What's Changed

* Description #PR by @author (co-authored by @user1 @Copilot)

### Documentation Updates

* Description #PR by @author

### Test Improvements

* Description #PR by @author

### Repository Infrastructure Updates

* Description #PR by @author

### Acknowledgements

* @user made their first contribution in #PR
* @user1 @user2 @user3 reviewed pull requests

**Full Changelog**: https://github.com/modelcontextprotocol/csharp-sdk/compare/{previous-tag}...v{version}
<!-- Example: https://github.com/modelcontextprotocol/csharp-sdk/compare/v1.3.0...v2.0.0-preview.1 -->

---

## API Compatibility Report

[ApiCompat output — pass/fail status per package and any issues or suppressions]

## API Diff Report

### ModelContextProtocol.Core

[API diff — additions, removals, changes]

### ModelContextProtocol

[API diff — additions, removals, changes]

### ModelContextProtocol.AspNetCore

[API diff — additions, removals, changes]
```

## Release Notes Template

The release notes section within the PR description uses the same format as the final GitHub release notes (used by the **publish-release** skill). This ensures consistency between the PR and the published release. Tag examples such as `v2.0.0-preview.1` are valid and should be used verbatim when the version has a prerelease suffix.

Omit empty sections. The preamble is **always required** — it is not inside a section heading.

```markdown
[Preamble — REQUIRED. Summarize the release theme.]

## Breaking Changes

Refer to the [C# SDK Versioning](https://csharp.sdk.modelcontextprotocol.io/versioning.html) documentation for details on versioning and breaking change policies.

1. **Description #PR**
   * Detail of the break
   * Migration guidance

## What's Changed

* Description #PR by @author (co-authored by @user1 @Copilot)

## Documentation Updates

* Description #PR by @author (co-authored by @user1 @Copilot)

## Test Improvements

* Description #PR by @author (co-authored by @user1 @Copilot)

## Repository Infrastructure Updates

* Description #PR by @author (co-authored by @user1 @Copilot)

## Acknowledgements

* @user made their first contribution in #PR
* @user submitted issue #1234 (resolved by #5678)
* @user1 @user2 @user3 reviewed pull requests

**Full Changelog**: https://github.com/modelcontextprotocol/csharp-sdk/compare/{previous-tag}...v{version}
<!-- Example: https://github.com/modelcontextprotocol/csharp-sdk/compare/v1.3.0...v2.0.0-preview.1 -->
```
