# Release Process

The following process is used when publishing new releases to NuGet.org.

The [`release-manager`](agents/release-manager.agent.md) custom agent orchestrates this process end
to end -- routing to the `prepare-release` and `publish-release` skills, holding a human gate at each
stage, tracking how long each stage takes, and closing with a release summary. Start it with a
prompt like "Where are we in the release process?" or "Prepare a release." The steps
below remain the authoritative description of the process itself.

## 1. Ensure the CI workflow is fully green

- Some integration tests are flaky and may require re-running
- Once the state of the branch is known to be good, a release can proceed
- **The release workflow _does not_ run tests** — CI must be green before starting

## 2. Prepare the release

From a local clone of the repository, use Copilot CLI to invoke the `prepare-release` skill. The skill assesses the semantic version, bumps the version in [`src/Directory.Build.props`](../src/Directory.Build.props), runs API compatibility checks, reviews documentation, drafts release notes, and creates a pull request with all release artifacts.

As part of Step 9 (documentation review), the skill also updates the shared embedded NuGet README (`src/PACKAGE.md`) -- adding any newly introduced packages to the package-list closure, applying the correct badge style (`nuget/vpre` for a prerelease series or `nuget/v` for a stable release), adding a release-notes link pointing to the tag being created, and syncing the same closure changes to the root `README.md`.

Review the PR, request changes if needed, and merge when ready.

## 3. Publish the release

After the prepare-release PR is merged, invoke the `publish-release` skill. The skill checks for any late-arriving PRs that could affect the release, refreshes the release notes, re-runs the README content checklist (confirming package closure, badge style, and release-notes link), and creates a **draft** GitHub release.

Review the draft release on GitHub, check 'Set as a pre-release' if appropriate, and click 'Publish release'.

## Branching

The `main` branch supports ongoing next-version feature development and owns shared AI release-process instructions; this does not prescribe a next-MAJOR version plan. Nightly `cron` CI on `main` publishes CI-suffixed packages to GitHub Packages.
Long-lived `release/{MAJOR}.x` branches track the latest shipped MINOR within each MAJOR and supply documentation content when a branch refresh is selected. Every push to a `release/*` branch publishes a CI-suffixed package to GitHub Packages, so servicing CI packages are commit-driven rather than clock-driven.
Initial release-branch establishment starts at an explicitly approved baseline and may require separate creation/push authorization. Every subsequent change to `release/*` — fixes, docs, infrastructure, versions, or promotions — goes through a reviewed work-branch PR, never direct commits/pushes to the servicing branch. Promotion to an approved newer MINOR preserves servicing ancestry through that PR, not reset/recreation or force-push.
Keep shared AI release automation skills and instructions on trusted `main`; servicing children follow the current main-based instructions, not stale or untrusted forked skills. Do not copy shared process files into release content. Runtime docs scripts, fixture tests, and picker assets also come from current `main`, independent of the triggering workflow ref.
Short-lived `release-{version}` branches are local prepare-release work branches that become pull requests, such as `release-2.0.0-preview.1` or `release-1.3.1`.
Official NuGet.org publishes occur only when a GitHub Release is created from a branch's tag.
The prepare-release skill asks for the source/base branch first so the release PR targets the same line it assessed.
For the agent-facing, structured version of these rules, see [release-branches.md](skills/shared-resources/release-branches.md).

### Coordinated releases (opt-in)

When the user requests multiple lines ship together, keep each line's targets, versions, approved
SHAs, PRs, audits, and CI evidence separate, and prepare all approved SHA-pinned drafts before
any human publication. Publication is not atomic; agree on the publication order and intended
GitHub Latest designation. A newer combined docs deployment must demonstrably
cover all intended labels and recorded selected-content SHAs; a cancelled earlier run alone is not
success. Package checks remain exact-tag checks, and GitHub Latest is distinct from the docs default
by publication date. See the [shared coordinated-release guidance](skills/shared-resources/release-branches.md#coordinated-releases-opt-in).

## 4. Verify the release

Publishing the release triggers two workflows in parallel. Invoke the `verify-release` skill to
monitor both and confirm their published outputs:

- **Release** — produces build artifacts and publishes the NuGet packages to NuGet.org. If the job
  fails, troubleshoot and re-run the workflow as needed. Verify the package version becomes listed
  at [nuget.org/packages/ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol).
- **[Publish Docs](workflows/docs.yml)** — discovers published majors, labels, and default ordering
  from GitHub releases, then rebuilds every major's selected content and deploys it to
  [csharp.sdk.modelcontextprotocol.io](https://csharp.sdk.modelcontextprotocol.io). Verify the new
  version appears in the picker and its path reflects the selected source. Orchestration/scripts/
  tests/picker assets always come from current `main`, with its HEAD SHA logged separately from
  content SHAs. Release events and default manual runs use `release-tags`; a manual
  `docs_source=latest-branches` refresh uses `origin/release/{MAJOR}.x` for every published major.
  All selected refs resolve once to full SHAs and their `VersionPrefix` majors must match; missing
  refs or mismatches fail without fallback. After explicit approval, refresh with
  `gh workflow run docs.yml --ref main --field docs_source=latest-branches`, or select `release-tags`
  to rebuild published tag content. Branch mode permits post-release docs updates without a new
  release/tag. Package verification remains against the exact released tag/SHA.
