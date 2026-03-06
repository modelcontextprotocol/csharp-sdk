# Release Process

The following process is used when publishing new releases to NuGet.org.

## 1. Ensure the CI workflow is fully green

- Some integration tests are flaky and may require re-running
- Once the state of the branch is known to be good, a release can proceed
- **The release workflow _does not_ run tests** — CI must be green before starting

## 2. Prepare the release

From a local clone of the repository, use Copilot CLI to invoke the `prepare-release` skill. The skill assesses the semantic version, bumps the version in [`src/Directory.Build.props`](../../src/Directory.Build.props), runs API compatibility checks, reviews documentation, drafts release notes, and creates a pull request with all release artifacts.

Review the PR, request changes if needed, and merge when ready.

## 3. Publish the release

After the prepare-release PR is merged, invoke the `publish-release` skill. The skill checks for any late-arriving PRs that could affect the release, refreshes the release notes, and creates a **draft** GitHub release.

Review the draft release on GitHub, check 'Set as a pre-release' if appropriate, and click 'Publish release'.

## 4. Monitor the Release workflow

- After publishing, a workflow will produce build artifacts and publish the NuGet packages to NuGet.org
- If the job fails, troubleshoot and re-run the workflow as needed
- Verify the package version becomes listed at [nuget.org/packages/ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)
