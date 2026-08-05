---
name: verify-release
description: Verify a published release of the C# MCP SDK. Monitors the Release and Publish Docs workflows triggered by publishing a GitHub release, confirms the packages are listed on NuGet.org, and confirms the versioned documentation site reflects the release. Use when asked to verify a release, check whether a release published correctly, monitor the release or docs workflow, confirm packages on NuGet, or check whether the docs site updated.
compatibility: Requires gh CLI with repo access for workflow runs and releases, and network access to nuget.org and csharp.sdk.modelcontextprotocol.io.
---

# Verify Release

Verify that a published release of `modelcontextprotocol/csharp-sdk` fully shipped. Publishing a
GitHub release triggers **two workflows in parallel**, and the release is not done until both have
succeeded and both of their outputs are confirmed live.

| Workflow | File | Trigger | Produces |
|---|---|---|---|
| Release | [`.github/workflows/release.yml`](../../workflows/release.yml) | `release: published` | NuGet packages published to NuGet.org |
| Publish Docs | [`.github/workflows/docs.yml`](../../workflows/docs.yml) | `release: published` | The versioned docs site at <https://csharp.sdk.modelcontextprotocol.io> |

Use the shared [release branch reference](../shared-resources/release-branches.md) for branch roles
and release tag conventions.

> **Safety: This skill is read-only by default.** It inspects workflow runs, releases, and published
> artifacts. The only actions it may take are re-running a failed workflow or dispatching a docs
> refresh, and both require explicit user confirmation.

## Process

Work through each step sequentially. Present findings at each step and get user confirmation before
taking any action.

### Step 1: Identify the Release

The user may provide:
- **A version or tag** (e.g., `2.0.0-preview.1`, `v1.3.1`) — use directly
- **No context** — list recent releases with `gh release list --limit 10` and ask the user to select

Confirm the release is **published**, not a draft:

```
gh release view {tag} --json tagName,isDraft,isPrerelease,publishedAt,targetCommitish,url
```

If the release is still a draft, **stop**. Neither workflow has run — nothing is published, and no
verification is possible. Tell the user the draft must be published in the GitHub UI first, and
that publishing is a deliberate human action this skill will not perform.

Record the tag, the published timestamp, and the target commitish for the following steps.

### Step 2: Locate Both Workflow Runs

Find the runs triggered by publishing this release. A release-event run carries the **tag name in
`headBranch`**, which is an exact identifier — use it rather than correlating on timestamps:

```
gh run list --workflow release.yml --event release --branch v{version} --limit 5 --json databaseId,status,conclusion,headBranch,headSha,createdAt,url
gh run list --workflow docs.yml --event release --branch v{version} --limit 5 --json databaseId,status,conclusion,headBranch,headSha,createdAt,url
```

**Do not identify runs by "the most recent run" or "created at or after `publishedAt`."** Those
match any release published in the same window, so a concurrent or closely-following release —
including a servicing patch published from another branch minutes later — can be reported as this
release's result, showing a green run for the wrong tag. Confirm `headBranch` equals `v{version}`
on every run before evaluating it.

Cross-check `headSha` against the release's target commitish recorded in Step 1. A mismatch means
the tag moved between drafting and publishing, and the run validated something other than what was
reviewed — stop and report it rather than evaluating the run.

If more than one run matches the tag, the workflow was re-run; evaluate the **latest attempt** and
say that earlier attempts existed rather than silently reporting only the newest.

Present both runs with their status, conclusion, and URL. Watch them **together** — they run
concurrently and either can fail independently. Do not report success for the release until both
are accounted for.

If a run cannot be found for either workflow, report which one is missing and check whether the
workflow is disabled or whether its `if` repository guard excluded the run (both workflows only run
in the `modelcontextprotocol/csharp-sdk` repository, not in forks).

### Step 3: Evaluate the Release Workflow

Report the run's conclusion. If it failed, identify the failing job and step and summarize the
error:

```
gh run view {run-id} --log-failed
```

A failure here does **not** roll back the release — the GitHub release and its tag remain, and the
workflow is simply re-run once the cause is addressed. Re-running is safe and is usually the right
first move. Recommend it, but **do not re-run without explicit user confirmation**.

> **Never run `dotnet nuget push` and never handle NuGet API keys.** Package publishing happens only
> through the workflow.

### Step 4: Evaluate the Publish Docs Workflow

Report the run's conclusion, accounting for these docs-specific behaviors:

- **Superseded runs are not failures.** The workflow uses a `pages` concurrency group with
  `cancel-in-progress: true`. Every run rediscovers the current releases and rebuilds the whole site
  from scratch, so a newer run fully supersedes the one it cancels. Report a cancelled run as
  *superseded* and follow the newer run instead.
- **Version discovery reads published releases.** For each major version >= 1, the workflow takes
  the most recently published non-draft release tagged `v{MAJOR}.*`. A draft release contributes
  nothing.
- **Every major is rebuilt.** Each major's docs are built from that major's latest release tag into
  its own path (`/v1/`, `/v2/`). A new MAJOR adds a new path; the site root redirects to the newest
  release, prereleases included.
- **Orchestration comes from `main`.** The scripts and picker assets are always checked out from
  `main`, while each version's content comes from its release tag. A docs fix that lives only in a
  release branch will not affect orchestration.

If it failed, summarize the failing step. Common causes are a docs build failure in one version's
worktree (`make generate-docs`) or a Pages deployment error.

### Step 5: Confirm the Published Packages

Confirm the exact released version is listed for each shipping package on NuGet.org.

Listing can lag a successful workflow run by several minutes. If the workflow succeeded but the
version is not yet visible, say so explicitly and offer to re-check — **do not report this as a
failure**. Distinguish "published but not yet indexed" from "not published."

Report each package with its status, and flag any shipping package missing from the release.

### Step 6: Confirm the Documentation Site

Confirm <https://csharp.sdk.modelcontextprotocol.io> reflects this release:

1. **Version path** — the major-version path for this release (for example `/v2/`) is live and
   serving the new content.
2. **Version picker** — the picker offers this release's major version.
3. **Root redirect** — the site root redirects to the expected default version, which is the newest
   release by publish date, prereleases included.
4. **Versioning page** — the slugged versioning page for this release,
   `https://csharp.sdk.modelcontextprotocol.io/v{MAJOR}/versioning.html`, resolves. Release notes
   link to it from the Breaking Changes section, and for the first release of a new MAJOR that path
   only comes into existence with this workflow run. Confirm the release notes use the slugged form
   and not the unslugged `/versioning.html`, which tracks the site default and can silently repoint
   when a later MAJOR ships.

GitHub Pages caches aggressively, so a short delay after a successful deploy is normal.
Distinguish "deployed but not yet propagated" from "deployed wrong."

### Step 7: Report

Summarize the verification as a table covering both workflows and both published outputs, and state
plainly whether the release is fully verified or what remains outstanding.

| Check | Status |
|---|---|
| Release workflow | ✅ succeeded — {run URL} |
| Publish Docs workflow | ✅ succeeded — {run URL} |
| Packages on NuGet.org | ✅ {version} listed for all N packages |
| Docs site | ✅ `/v2/` live, picker updated, root redirects |

## Remediation

Both remediations require explicit user confirmation.

**Re-run a failed workflow:**

```
gh run rerun {run-id} --failed
```

**Refresh the docs without a new release** — when documentation content needs correcting after the
release, the docs workflow accepts a manual dispatch that rebuilds one major version's content from
an arbitrary ref, without minting a product release:

```
gh workflow run docs.yml --field docs_ref={branch-tag-or-commit}
```

The ref's major version, read from `src/Directory.Build.props`, must have a published release; the
workflow fails fast if it does not. This replaces only the matching major's HTML — orchestration
and all other versions are unaffected.

## Edge Cases

- **Release is still a draft** — stop; neither workflow has run. The user must publish in the GitHub UI.
- **Docs run cancelled** — expected under the `pages` concurrency group; report as superseded and follow the newer run.
- **Only one workflow ran** — check whether the other is disabled, or whether the repository guard excluded it (forks do not run either workflow).
- **Workflow succeeded but NuGet version not listed** — indexing lag; re-check before reporting a failure.
- **Workflow succeeded but docs not visible** — Pages caching; re-check before reporting a failure.
- **Docs site missing the new major version** — confirm the release is published and non-draft, then confirm the tag matches `v{MAJOR}.*`.
- **Root redirects to an unexpected version** — the default is the newest release *by publish date*, including prereleases. A prerelease published after a stable release becomes the default; this is by design.
- **Release workflow failed after partial publish** — some packages may already be on NuGet.org. NuGet versions cannot be unpublished; re-running skips already-published versions. Report exactly which packages are listed before recommending a re-run.
- **Versioning link is unslugged or points at the wrong MAJOR** — release notes must link to `/v{MAJOR}/versioning.html` for the released version. Report it so the user can correct the body; the unslugged form tracks the site default and will repoint when a later MAJOR ships.
- **Verifying an older release** — the docs workflow only ever reflects each major's *latest* release, so an older release's docs path will have been overwritten by a newer one. Verify packages only and note this.
