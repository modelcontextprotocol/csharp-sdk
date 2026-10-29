# Release Branches

Shared reference for release skills. Describes the branch roles used by the release workflow and the rules each skill follows for selecting a branch and looking up the previous release.

This guidance is release-independent. Do not encode particular releases' product inclusion/exclusion
decisions or one-off version/publication-order choices in documentation, agents, skills, or authored
planning artifacts, and do not relocate those decisions into another artifact when removing them.

## Branch roles

| Branch              | Purpose                                         | CI behavior                            |
| ------------------- | ----------------------------------------------- | -------------------------------------- |
| `main`              | Ongoing next-version feature development; shared AI release instructions | Nightly `cron` build → GitHub Packages |
| `release/{MAJOR}.x` | Latest shipped MINOR's servicing and docs content for a MAJOR | Every push → GitHub Packages           |
| `release-{version}` | Short-lived release preparation branch          | Built by PR CI; no package publishing  |

Official NuGet.org publishes happen only when a GitHub Release is created from a branch's tag.

Servicing branches follow the **latest shipped MINOR**, not the original `.0` line.
A `release/{MAJOR}.x` branch services the latest shipped MINOR and may promote to a newly approved
MINOR while preserving all servicing ancestry. This does not prescribe the next version on `main`.

### Servicing branch changes

Initial branch establishment starts at an explicitly approved baseline commit and may require
separate creation/push authorization. After establishment, **every change** to `release/*` — fixes, docs, infrastructure,
versions, and MINOR promotions — must arrive through a reviewed work-branch PR targeting that
servicing branch. Never commit or push changes directly to a servicing branch. Promotion preserves
servicing ancestry through that PR, never reset/recreation or a force-push.

Shared AI release automation skills and agent references stay on `main`, and are not copied into
release content. Runtime docs scripts, fixture tests, and picker assets also come from current
`main`; the triggering workflow must retain the checkout and source-selection contract.
Servicing children must receive and follow the current main-based instruction/reference version;
see [delegation](../../agents/release-manager/references/delegation.md#delegating-stage-1).

## Coordinated releases (opt-in)

Use this only when the user requests coordinated shipping of multiple lines. Keep each line's
target branch, version, approved full SHA, PR, audit range/results, and CI evidence separate.
All approved SHA-pinned drafts must exist before any is handed off for human publication.
Publishing is **not atomic**: agree on the order and verify each package run against its exact tag.

Agree on the intended GitHub **Latest** designation separately from the docs default, which follows
publication date. After publication, require a successful deployed combined docs run whose discovered
labels and recorded selected-content SHAs demonstrably cover all intended releases. Cancellation of an
earlier Pages run alone is not success; see [verification](../verify-release/SKILL.md#step-4-evaluate-the-publish-docs-workflow).

## Documentation content and orchestration

Release publication and manual docs refreshes discover published majors, labels, and default
ordering from GitHub release metadata. Release events and default manual runs use **release-tags**:
each major's discovered published tag supplies its content. A manual `docs_source=latest-branches`
run instead uses `origin/release/{MAJOR}.x` for every discovered major. The selection applies to all
majors, not just the triggering release. Fetch refs, resolve every selected ref once to a full SHA,
validate its `VersionPrefix` major against the slug, and build that immutable SHA. Missing selected
refs and mismatches fail explicitly; never silently substitute another source or `main`.

The workflow explicitly checks out **current `main`** for scripts, fixture tests, and picker assets,
regardless of the triggering release tag or manual ref. Log that orchestration HEAD SHA separately
from each selected content ref/SHA. Do not copy product docs/source or shared AI process guidance
merely to propagate runtime infrastructure. Preserve existing Pages environment rules, permissions,
and repository guards; selecting a ref does not authorize deployment or bypass protections.

After explicit approval, use `gh workflow run docs.yml --ref main --field docs_source=release-tags`
to rebuild tag content or `--field docs_source=latest-branches` for a content-only branch refresh.
Branch-sourced docs can advance beyond a package tag without requiring a new product release/tag;
picker labels still come from published metadata. Verify docs against the selected source's logged
SHA. Package verification remains pinned to the exact released tag/SHA and package version.

## Selecting a source/base branch (`prepare-release` Step 1)

1. List candidate branches via:
   `gh api repos/{owner}/{repo}/branches --paginate --jq '[.[] | select(.name == "main" or (.name | startswith("release/"))) | .name]'`
2. Present the list to the user. Default selection: `main`.
3. The selected branch drives:
   - Previous-release lookup (see below).
   - The branch on which the candidate version is read from `src/Directory.Build.props`.
   - The commit range from which PRs are collected.
   - The `--base` of the PR created at the end of the skill.

## Previous-release tag lookup

Select the **highest semver** among published releases that are **ancestors of the target commit**,
excluding drafts:

```sh
gh release list --exclude-drafts --limit 50
```

- On `main`: no MAJOR filter — the highest semver ancestor wins.
- On `release/{MAJOR}.x`: restrict candidates to tags matching `v{MAJOR}.*`.

**"Highest semver" and "most recent by date" are not the same rule, and the difference is not
hypothetical.** A lower-version servicing release can be published after a higher-version
development-line release. Ordering by publication date can pick a tag that is not on `main` at all,
which produces a bogus PR range and makes ApiCompat
report the entire API surface as removed. Order by version, not by publication time.

The ancestry constraint is what makes this safe across branches, so verify it rather than assuming
the version ordering implied it — a tag can be both the highest semver and unreachable from the
target. `prepare-release` Step 2 performs this check explicitly.

This is purely a baseline-selection rule. It does **not** change the breaking-change policy. See [the versioning docs](https://csharp.sdk.modelcontextprotocol.io/versioning.html) for the policy.

## Versioning documentation links

The documentation site is published per major version under a `v{MAJOR}` slug (`/v1/`, `/v2/`). Any
link to the versioning documentation from **release notes** — both the release-notes link and the
paragraph under the `## Breaking Changes` heading — must point at the slugged instance for the
version being released:

```
https://csharp.sdk.modelcontextprotocol.io/v{MAJOR}/versioning.html
```

The slug is derived from the **MAJOR component of the version being released**, not from the branch:

| Version being released | Versioning link |
| ---------------------- | --------------- |
| `1.3.1`                | `https://csharp.sdk.modelcontextprotocol.io/v1/versioning.html` |
| `2.0.0-preview.1`      | `https://csharp.sdk.modelcontextprotocol.io/v2/versioning.html` |
| `2.0.0`                | `https://csharp.sdk.modelcontextprotocol.io/v2/versioning.html` |

The released version determines the slug. In branch mode, `release/{MAJOR}.x` supplies content for
that MAJOR; in tag mode the discovered published tag supplies it. The docs workflow fails if the
selected source's `VersionPrefix` has a different MAJOR.

Prerelease suffixes do not affect the slug: `2.0.0-preview.1` and `2.0.0` both use `/v2/`.

The unslugged `https://csharp.sdk.modelcontextprotocol.io/versioning.html` redirects to the site's
default version, which tracks the newest release. It is therefore unstable for a published release's
notes — a later MAJOR would silently repoint it. Never use the unslugged form in release notes.

**First release of a new MAJOR**: the `/v{MAJOR}/` path does not exist until the Publish Docs
workflow runs, which happens when the GitHub release is published. The link is forward-referencing
at prepare and publish time, exactly like the release-notes tag link, and resolves once the release
is published. The **verify-release** skill confirms it.

Prepare-release work branches are named `release-{version}` (flat, hyphen-separated):
- `release-2.0.0-preview.1`
- `release-1.3.1`
- `release-2.0.0`

Hyphens in prerelease versions are valid in git branch names.
