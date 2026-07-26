---
name: publish-release
description: Publish a GitHub release for the C# MCP SDK after a prepare-release PR has been merged. Refreshes release notes to include any PRs merged since preparation, warns about version or breaking change impacts from late-arriving PRs, and creates a draft GitHub release. Use when asked to publish a release, finalize a release, create release notes, or complete a release after the prepare-release PR has been merged.
compatibility: Requires gh CLI with repo access and GitHub API access for PR details, timeline events, and commit trailers.
---

# Publish Release

Create a GitHub release for the `modelcontextprotocol/csharp-sdk` repository after a **prepare-release** PR has been merged. This skill refreshes the release notes to include any PRs merged between the preparation branch point and the merge, warns about changes that affect the version or breaking change assessment, and creates a **draft** GitHub release.

Use the shared [release branch reference](../shared-resources/release-branches.md) for branch roles, previous-release lookup rules, and release work-branch naming.

> **Safety: This skill only creates and updates draft releases. It must never publish a release.** If the user asks to publish, decline and instruct them to publish manually through the GitHub UI.

## Process

Work through each step sequentially. Present findings at each step and get user confirmation before proceeding.

### Step 1: Identify the Prepare-Release PR

The user may provide:
- **A PR number or URL** — use directly
- **A version number** (e.g., `1.1.0`, `2.0.0-preview.1`) — search for a merged PR titled `Release v{version}`. Prerelease versions are used verbatim, for example `Release v2.0.0-preview.1`
- **No context** — list recently merged PRs with `Release v` in the title and ask the user to select

Verify the PR is merged. Extract:
- The release version from the PR title and branch name
- The merge commit SHA
- The PR description (which contains draft release notes, ApiCompat, and ApiDiff from the **prepare-release** skill)

### Step 2: Determine Version and Commit Range

1. Read `src/Directory.Build.props` at the merge commit to confirm `<VersionPrefix>` and `<VersionSuffix>`. The tag is `v{VersionPrefix}` plus `-{VersionSuffix}` when the suffix is present; for example, `<VersionPrefix>2.0.0</VersionPrefix>` + `<VersionSuffix>preview.1</VersionSuffix>` → `v2.0.0-preview.1`.
2. Determine the previous release tag from `gh release list` (most recent **published** release — exclude drafts with `--exclude-drafts`). The lookup is branch-aware: when the merge commit is on a `release/{MAJOR}.x` branch, restrict candidates to tags matching `v{MAJOR}.*`; on `main`, use the most recent published release globally. See [release-branches.md](../shared-resources/release-branches.md).
3. Identify the full commit range: previous release tag → merge commit.

### Step 3: Check for Additional PRs

Compare the PRs included in the original prepare-release PR description with the full set of PRs now merged in the commit range. Use the [SemVer assessment guide](../bump-version/references/semver-assessment.md) (owned by the **bump-version** skill) to evaluate the impact of any new PRs against the version that was committed during preparation, including its prerelease and branch-context computation rules. This is not a policy change; only the version computation and previous-release lookup change.

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
3. **Re-attribute** co-authors for any new PRs by harvesting `Co-authored-by` trailers from all commits in each PR.
4. **Update acknowledgements** to include contributors from new PRs.

### Step 5: Review README and Validate Code Samples

Re-run the README content checklist from [../prepare-release/references/readme-content.md](../prepare-release/references/readme-content.md) and validate code samples against the current SDK at the merge commit. Produce final suggestions before the release is created.

1. **Content checklist** -- Open `src/PACKAGE.md` and verify:
   - **Package-list closure**: every shipping SDK package is listed. If a new package was introduced after prepare-release ran, it may be missing.
   - **Badge strategy**: all badges use `nuget/vpre` for a prerelease or `nuget/v` for a stable release. Verify the badge style is correct for this release type.
   - **Release-notes link**: the link points to `https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v{version}` for the tag being created. The tag is about to exist -- verify the URL is correct.
   - **Root README.md sync**: confirm the root `README.md` package list is aligned.
2. **Snippet validation** -- Extract `csharp`-fenced code blocks from `src/PACKAGE.md` and `README.md`, build the temporary test project, and report results. Follow [../prepare-release/references/readme-snippets.md](../prepare-release/references/readme-snippets.md) for the full procedure.
3. **Delete** the temporary project after validation.

If issues are found, present them to the user with proposed fixes. Any fixes must be applied as a separate commit before the draft release is created.

**Edge Cases:**
- **Stale package closure** -- A package introduced between prepare-release and now may not be listed. Add it to `src/PACKAGE.md` and `README.md`.
- **Wrong badge style for the release type** -- Switch all badges together from `nuget/vpre` to `nuget/v` (or vice versa) if the prepare-release step used the wrong style.
- **Missing or incorrect release-notes link** -- Correct the link to target the exact tag being created, including any prerelease suffix.

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
- **Title / Tag**: the confirmed tag, including any prerelease suffix (e.g. `v1.3.1`, `v2.0.0-preview.1`)
- **Target**: merge commit SHA, its message, the merge commit's branch (the prepare-release PR base), and the prepare-release PR link

After confirmation:
- Create with `gh release create --draft {tag} --target {merge-commit-branch}` (always `--draft`), using the prerelease tag verbatim when present
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
- **Copilot timeline missing**: fall back to `Co-authored-by` trailers to determine whether `@Copilot` should be a co-author; if still unclear, use `@Copilot` as primary author
- **No breaking changes**: omit the Breaking Changes section entirely
- **Single breaking change**: use the same numbered format as multiple

## Release Notes Template

Omit empty sections. The preamble is **always required** — it is not inside a section heading. Tags may include prerelease suffixes, such as `v2.0.0-preview.1`, and Full Changelog compare links should use the exact tag.

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
