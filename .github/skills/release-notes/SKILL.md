---
name: release-notes
description: Prepare release notes for the C# MCP SDK. Categorizes PRs, audits breaking changes, reconciles labels, attributes co-authors, identifies acknowledgements, and creates or updates a draft GitHub release. Use when asked to prepare a release, write release notes, update release notes, create a changelog, or summarize a version.
compatibility: Requires gh CLI with repo access and GitHub API access for PR details, timeline events, and commit trailers.
---

# Release Notes Preparation

Prepare polished, categorized release notes for the `modelcontextprotocol/csharp-sdk` repository. This skill determines the version from the repository's build properties, gathers all data from GitHub, and creates or updates a **draft** release.

> **Safety: This skill only creates and updates draft releases. It must never publish a release.** If the user asks to publish, decline and instruct them to publish manually through the GitHub UI.

## Process

Work through each step sequentially. Present findings at each step and get user confirmation before proceeding. Skip any step that has no applicable items.

### Step 1: Determine Version and Target

The user may provide:
- **A git ref** (commit SHA, branch, or tag) — use as the target commit
- **An existing draft release** (URL or tag) — use its target commit
- **No context** — show the last 5 commits on `main` (noting HEAD) and offer the option to enter a branch or tag name instead

Once the target is established:
1. Read `src/Directory.Build.props` **at the target commit**. Extract `<VersionPrefix>` and `<VersionSuffix>`. The tag and title are `v{VersionPrefix}-{VersionSuffix}`, or `v{VersionPrefix}` if no suffix. Pre-release if `VersionSuffix` is present.
2. Check for an existing draft release matching this tag.
3. Determine the previous release tag from `gh release list` (most recent published release).
4. Get the full list of PRs merged between the previous release tag and the target commit.

### Step 2: Categorize and Attribute

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
- Harvest `Co-authored-by` trailers from all commits in each PR's merge commit
- For Copilot-authored PRs, check the `copilot_work_started` timeline event to identify the triggering user. That person becomes the primary author; `@Copilot` becomes a co-author
- Omit the co-author parenthetical when there are none
- Sort entries within each section by merge date (chronological)

### Step 3: Breaking Change Audit

Invoke the **breaking-changes** skill with the commit range from the previous release tag to the target commit. The full audit process applies whether creating new release notes or editing existing ones — examine every PR, assess impact, reconcile labels (offering to add/remove labels and comment on PRs), and get user confirmation.

When **editing an existing release**, also extract any breaking changes already documented in the current release notes (`## Breaking Changes` section). These must be preserved — never remove breaking changes from existing notes. Reconcile the existing documented breaks with the audit results:
- **Previously documented breaks**: keep them, updating formatting if needed
- **Newly discovered breaks**: add them alongside the existing ones
- **Audit finds no issue with a documented break**: still keep it (do not remove without explicit user request)

Use the results (confirmed breaking changes with impact ordering and detail bullets) in the remaining steps.

### Step 4: Validate README Code Samples

Verify that all C# code samples in the package README files compile against the current SDK at the target commit. Follow [references/readme-snippets.md](references/readme-snippets.md) for the full procedure.

1. Extract `csharp`-fenced code blocks from `README.md`, `src/ModelContextProtocol.Core/README.md`, and `src/ModelContextProtocol.AspNetCore/README.md`
2. Create a temporary test project at `tests/ReadmeSnippetValidation/` that references the SDK projects
3. Wrap each code block in a compilable method, applying fixups for incomplete snippets (replace `...` with `null!`, add common usings)
4. Build the project with `dotnet build`
5. Report results — classify any failures as API mismatches (README bugs) or structural fragments
6. If API mismatches are found, propose fixes and get user confirmation before editing READMEs
7. Delete the temporary `tests/ReadmeSnippetValidation/` directory

### Step 5: Review Sections

Present each section for user review:
1. **Breaking Changes** — sorted most → least impactful (from Step 3 results)
2. **What's Changed** — chronological; includes breaking change PRs
3. **Documentation Updates** — chronological
4. **Test Improvements** — chronological
5. **Repository Infrastructure Updates** — chronological

### Step 6: Acknowledgements

Identify contributors beyond PR authors:
1. **New contributors** — first contribution to the repository in this release
2. **Issue reporters** — users who submitted issues resolved by PRs in this release (cite the resolving PR)
3. **PR reviewers** — users who reviewed PRs, excluding PR authors and bots

Exclude anyone already listed as a PR author. Format:
```
* @user made their first contribution in #PR
* @user submitted issue #1234 (resolved by #5678)
* @user1 @user2 @user3 reviewed pull requests
```
Reviewers go on a single bullet, sorted by number of PRs reviewed (most first), without citing the count.

### Step 7: Preamble

Every release **must** have a preamble — a short paragraph summarizing the release theme that appears before the first `##` heading. The preamble is not optional. The preamble may mention the presence of breaking changes as part of the theme summary, but the versioning documentation link belongs under the Breaking Changes heading (see template), not in the preamble.

- **New release**: Draft a preamble based on the categorized changes.
- **Editing an existing release**: Extract the current preamble from the release body (text before the first `##` heading) and present it alongside a newly drafted alternative.

Present the options and let the user choose one, edit one, or enter their own text or markdown.

### Step 8: Final Assembly

1. Combine the confirmed preamble with all sections from previous steps.
2. **Notable callouts** — only if something is extraordinarily noteworthy.
3. Present the **complete release notes** for user approval.

Follow [references/formatting.md](references/formatting.md) when composing and updating the release body.

### Step 9: Create or Update Draft Release

Display release metadata for user review:
- **Title / Tag**: e.g. `v0.9.0-preview.1`
- **Target**: commit SHA, its message, and associated PR link
- **Newer commits**: whether `main` has commits beyond the target
- **Pre-release**: yes/no (based on `VersionSuffix`)

After confirmation:
- **No existing draft** → create with `gh release create --draft` (always `--draft`)
- **Existing draft** → update the release notes body only
- **Never publish.** Never change pre-release state, title, tag, or other metadata on an existing draft.

When the user requests revisions after the initial update, always rewrite the complete body as a file — never perform in-place string replacements. See [references/formatting.md](references/formatting.md).

### Step 10: Bump Version

After the draft release is created or updated, inform the user that the draft release is now associated with a known target commit (state the short SHA and commit message) and recommend proceeding with a version bump. Ask if they want to create a pull request to bump the version for the next release. If yes, invoke the **bump-version** skill and let it handle determining or requesting the target version number.

## Edge Cases

- **PR spans categories**: categorize by primary intent
- **Copilot timeline missing**: fall back to `Co-authored-by` trailers; if still unclear, use `@Copilot` as primary author
- **Draft tag changes**: re-fetch the tag before each `gh release edit`
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
