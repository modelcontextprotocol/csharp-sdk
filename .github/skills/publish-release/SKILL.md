---
name: publish-release
description: Publish a GitHub release for the C# MCP SDK after a prepare-release PR has been merged. Refreshes release notes to include any PRs merged since preparation, warns about version or breaking change impacts from late-arriving PRs, and creates a draft GitHub release. Use when asked to publish a release, finalize a release, create release notes, or complete a release after the prepare-release PR has been merged.
compatibility: Requires gh CLI with repo access and GitHub API access for PR details, timeline events, and commit trailers.
---

# Publish Release

Create a GitHub release for the `modelcontextprotocol/csharp-sdk` repository after a **prepare-release** PR has been merged. This skill refreshes the release notes to include any PRs merged between the preparation branch point and the merge, warns about changes that affect the version or breaking change assessment, and creates a **draft** GitHub release.

> **Safety: This skill only creates and updates draft releases. It must never publish a release.** If the user asks to publish, decline and instruct them to publish manually through the GitHub UI.

## Process

Work through each step sequentially. Present findings at each step and get user confirmation before proceeding.

### Step 1: Identify the Prepare-Release PR

The user may provide:
- **A PR number or URL** — use directly
- **A version number** (e.g., `1.1.0`) — search for a merged PR titled `Release v{version}`
- **No context** — list recently merged PRs with `Release v` in the title and ask the user to select

Verify the PR is merged. Extract:
- The release version from the PR title and branch name
- The merge commit SHA
- The PR description (which contains draft release notes, ApiCompat, and ApiDiff from the **prepare-release** skill)

### Step 2: Determine Version and Commit Range

1. Read `src/Directory.Build.props` at the merge commit to confirm `<VersionPrefix>` and `<VersionSuffix>`. The tag is `v{VersionPrefix}-{VersionSuffix}`, or `v{VersionPrefix}` if no suffix. Pre-release if `VersionSuffix` is present.
2. Determine the previous release tag from `gh release list` (most recent **published** release — exclude drafts with `--exclude-drafts`).
3. Identify the full commit range: previous release tag → merge commit.

### Step 3: Check for Additional PRs

Compare the PRs included in the original prepare-release PR description with the full set of PRs now merged in the commit range. Use the [SemVer assessment guide](../bump-version/references/semver-assessment.md) (owned by the **bump-version** skill) to evaluate the impact of any new PRs against the version that was committed during preparation.

1. Extract the PR list from the prepare-release PR description (all `#NNN` references in release notes sections).
2. Get the full set of PRs merged between the previous release tag and the merge commit.
3. Identify any **new PRs** — PRs present in the full range but not referenced in the prepare-release description.

If new PRs exist, **warn the user** with details for each new PR:

> ⚠️ **New PRs merged since release preparation:**
>
> * #NNN — Title (@author) — [impact assessment]

For each new PR, assess and flag impacts:

- **Breaking changes** — Does this PR introduce breaking changes not covered by the original audit? If yes, **warn** that the semantic version may need to be re-assessed. This is a **critical warning** — the release version may be incorrect.
- **API surface changes** — Does this PR add new public APIs? If yes, warn that the ApiCompat and ApiDiff results in the prepare-release PR are stale and should not be relied upon.
- **Version impact** — Does this PR change the SemVer level (e.g., what was assessed as PATCH now warrants MINOR, or MINOR now warrants MAJOR)?

If any new PRs have version-level or breaking change impacts, **strongly recommend** that the user either:
1. **Abort** and re-run the **prepare-release** skill to produce an updated release PR with correct version, ApiCompat, and ApiDiff results
2. **Acknowledge** the impacts and proceed with the current version, documenting the decision in the release notes

The user must explicitly choose an option before proceeding.

### Step 4: Refresh Release Notes

Re-categorize all PRs in the commit range (including any new ones from Step 3). See the [categorization guide](../prepare-release/references/categorization.md) for detailed guidance.

1. **Re-run the breaking change audit** using the **breaking-changes** skill if new PRs were found that may introduce breaks. Otherwise, carry forward the results from the prepare-release PR.
2. **Re-categorize** all PRs into sections (What's Changed, Documentation, Tests, Infrastructure).
3. **Re-attribute** co-authors for any new PRs.
4. **Update acknowledgements** to include contributors from new PRs.

### Step 5: Validate README Code Samples

Verify that all C# code samples in the package README files compile against the current SDK at the merge commit. Follow the [README validation guide](../prepare-release/references/readme-snippets.md) for the full procedure.

1. Extract `csharp`-fenced code blocks from `README.md` and `src/PACKAGE.md`
2. Create a temporary test project at `tests/ReadmeSnippetValidation/`
3. Build and report results
4. Delete the temporary project

### Step 6: Review Sections

Present each section for user review:
1. **Breaking Changes** — sorted most → least impactful
2. **What's Changed** — chronological
3. **Documentation Updates** — chronological
4. **Test Improvements** — chronological
5. **Repository Infrastructure Updates** — chronological
6. **Acknowledgements**

Highlight any changes from the prepare-release draft (new entries, reordered entries, updated descriptions) so the user can see what's different.

### Step 7: Preamble

Every release **must** have a preamble — a short paragraph summarizing the release theme that appears before the first `##` heading. The preamble is not optional. The preamble may mention the presence of breaking changes as part of the theme summary, but the versioning documentation link belongs under the Breaking Changes heading (see template), not in the preamble.

Extract the draft preamble from the prepare-release PR description and present it alongside a freshly drafted alternative (accounting for any new PRs).

Present both options and let the user choose one, edit one, or enter their own text or markdown.

### Step 8: Final Assembly

1. Combine the confirmed preamble with all sections from previous steps.
2. **Notable callouts** — only if something is extraordinarily noteworthy.
3. Present the **complete release notes** for user approval.

Follow [references/formatting.md](references/formatting.md) when composing and updating the release body.

### Step 9: Create Draft Release

Display release metadata for user review:
- **Title / Tag**: the confirmed version (e.g. `v1.1.0`)
- **Target**: merge commit SHA, its message, and the prepare-release PR link
- **Pre-release**: yes/no (based on `VersionSuffix`)

After confirmation:
- Create with `gh release create --draft` (always `--draft`)
- **Never publish.** If the user asks to publish, decline and instruct them to publish manually.

When the user requests revisions after the initial creation, always rewrite the complete body as a file — never perform in-place string replacements. See [references/formatting.md](references/formatting.md).

## Edge Cases

- **No new PRs since preparation**: proceed normally — the prepare-release notes are used as the foundation with no warnings
- **New PR introduces breaking changes**: strongly recommend aborting and re-running prepare-release; if user chooses to proceed, document the decision and update the breaking changes section
- **New PR changes version level**: warn that the release tag may not match the expected SemVer level; recommend re-running prepare-release
- **Prepare-release PR description is malformed**: fall back to gathering all data fresh from the commit range
- **PR not found**: if the prepare-release PR cannot be identified, offer to proceed manually by specifying a version and target commit
- **Draft already exists**: if a draft release with the same tag already exists, offer to update it
- **PR spans categories**: categorize by primary intent
- **Copilot timeline missing**: fall back to `Co-authored-by` trailers; if still unclear, use `@Copilot` as primary author
- **No breaking changes**: omit the Breaking Changes section entirely
- **Single breaking change**: use the same numbered format as multiple

## Release Notes Template

Omit empty sections. The preamble is **always required** — it is not inside a section heading.

```markdown
[Preamble — REQUIRED. Summarize the release theme.]

## Breaking Changes

Refer to the [C# SDK Versioning](https://modelcontextprotocol.github.io/csharp-sdk/versioning.html) documentation for details on versioning and breaking change policies.

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

**Full Changelog**: https://github.com/modelcontextprotocol/csharp-sdk/compare/previous-tag...new-tag
```
