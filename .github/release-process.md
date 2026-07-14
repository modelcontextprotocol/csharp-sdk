# Release Process

The following process is used when publishing new releases to NuGet.org.

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

The `main` branch is the next-MAJOR preview and development line; currently, it produces the `2.0.0-preview.*` series. Nightly `cron` CI on `main` publishes CI-suffixed packages to GitHub Packages.
Long-lived `release/{MAJOR}.x` branches are created on demand when a shipped MAJOR needs servicing releases. Every push to a `release/*` branch publishes a CI-suffixed package to GitHub Packages, so servicing CI packages are commit-driven rather than clock-driven.
Short-lived `release-{version}` branches are local prepare-release work branches that become pull requests, such as `release-2.0.0-preview.1` or `release-1.3.1`.
Official NuGet.org publishes occur only when a GitHub Release is created from a branch's tag.
The prepare-release skill asks for the source/base branch first so the release PR targets the same line it assessed.
For the agent-facing, structured version of these rules, see [release-branches.md](skills/shared-resources/release-branches.md).

## 4. Monitor the Release workflow

- After publishing, a workflow will produce build artifacts and publish the NuGet packages to NuGet.org
- If the job fails, troubleshoot and re-run the workflow as needed
- Verify the package version becomes listed at [nuget.org/packages/ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)
