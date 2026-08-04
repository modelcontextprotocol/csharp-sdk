# Release Branches

Shared reference for release skills. Describes the branch roles used by the release workflow and the rules each skill follows for selecting a branch and looking up the previous release.

## Branch roles

| Branch              | Purpose                                         | CI behavior                            |
| ------------------- | ----------------------------------------------- | -------------------------------------- |
| `main`              | Next-MAJOR preview/development line             | Nightly `cron` build → GitHub Packages |
| `release/{MAJOR}.x` | Long-lived servicing branch for a shipped MAJOR | Every push → GitHub Packages           |
| `release-{version}` | Short-lived release preparation branch          | Built by PR CI; no package publishing  |

Official NuGet.org publishes happen only when a GitHub Release is created from a branch's tag.

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

- On `main`: most recent published release globally (use `gh release list --exclude-drafts --limit 50` and pick the highest semver). No MAJOR filter.
- On `release/{MAJOR}.x`: most recent published release whose tag matches `v{MAJOR}.*`. Drafts are excluded.

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

The branch is normally consistent with this — `release/1.x` releases `1.x` versions and `main`
currently releases `2.x` — but the version is what determines the slug. If a release's MAJOR ever
disagrees with its branch's MAJOR, follow the version.

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
