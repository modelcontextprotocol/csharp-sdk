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

Sort every PR into one of three categories. See [references/categorization.md](references/categorization.md) for detailed guidance.

| Category | Content |
|----------|---------|
| **What's Changed** | Features, bug fixes, improvements, breaking changes |
| **Documentation Updates** | PRs whose sole purpose is documentation |
| **Repository Infrastructure Updates** | CI/CD, tests, dependency bumps, version bumps |

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

Examine **every** PR for breaking changes — both API (compile-time) and behavioral (runtime). For each PR, study the description, linked issues, review comments, and full diff.

For each identified breaking change, assess **impact** (breadth of consumers affected, compile-time vs. behavioral, migration difficulty) to inform the ordering in Step 5.

Compare findings against existing `breaking-change` labels. See [references/breaking-changes.md](references/breaking-changes.md) for classification guidance.

### Step 4: Reconcile Labels

Present mismatches to the user interactively:

- **Unlabeled but appears breaking** → explain why, ask user to confirm. If confirmed: apply label, ask user whether to comment on the PR explaining the addition, and include in Breaking Changes.
- **Labeled but does not appear breaking** → explain why, ask user to confirm removal. If confirmed: remove label, ask user whether to comment on the PR explaining the removal, and exclude from Breaking Changes.

### Step 5: Review Sections

Present each section for user review:
1. **Breaking Changes** — sorted most → least impactful (from Step 3 assessment)
2. **What's Changed** — chronological; includes breaking change PRs
3. **Documentation Updates** — chronological
4. **Repository Infrastructure Updates** — chronological

### Step 6: Acknowledgements

Identify contributors beyond PR authors:
1. **New contributors** — first contribution to the repository in this release
2. **Issue reporters** — users who submitted issues resolved by PRs in this release (cite the resolving PR)
3. **PR reviewers** — users who reviewed PRs, excluding PR authors and bots

Exclude anyone already listed as a PR author. Format:
```
* @user made their first contribution in #PR
* @user submitted issue #1234 (resolved by #5678)
* PR reviewers: @user1 @user2 @user3
```
Reviewers go on a single bullet, sorted by number of PRs reviewed (most first), without citing the count.

### Step 7: Final Assembly

1. **Preamble** — summarize the release theme. If there are breaking changes, mention them and link to the [C# SDK Versioning](https://modelcontextprotocol.github.io/csharp-sdk/versioning.html) docs. Do not repeat preamble text under the Breaking Changes heading. Let the user revise or omit.
2. **Notable callouts** — only if something is extraordinarily noteworthy.
3. Present the **complete release notes** for user approval.

Follow [references/formatting.md](references/formatting.md) when composing and updating the release body.

### Step 8: Create or Update Draft Release

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

## Edge Cases

- **PR spans categories**: categorize by primary intent
- **Copilot timeline missing**: fall back to `Co-authored-by` trailers; if still unclear, use `@Copilot` as primary author
- **Draft tag changes**: re-fetch the tag before each `gh release edit`
- **No breaking changes**: omit the Breaking Changes section and breaking change preamble language
- **Single breaking change**: use the same numbered format as multiple

## Release Notes Template

Omit empty sections:

```markdown
[Preamble — release theme, breaking changes note, versioning docs link]

## Breaking Changes

1. **Description #PR**
   * Detail of the break
   * Migration guidance

## What's Changed

* Description #PR by @author (co-authored by @user1 @Copilot)

## Documentation Updates

* Description #PR by @author (co-authored by @user1 @Copilot)

## Repository Infrastructure Updates

* Description #PR by @author (co-authored by @user1 @Copilot)

## Acknowledgements

* @user made their first contribution in #PR
* @user submitted issue #1234 (resolved by #5678)
* PR reviewers: @user1 @user2 @user3

**Full Changelog**: previous-tag...new-tag
```
