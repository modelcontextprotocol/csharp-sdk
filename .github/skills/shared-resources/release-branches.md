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

## Work-branch naming

Prepare-release work branches are named `release-{version}` (flat, hyphen-separated):
- `release-2.0.0-preview.1`
- `release-1.3.1`
- `release-2.0.0`

Hyphens in prerelease versions are valid in git branch names.
