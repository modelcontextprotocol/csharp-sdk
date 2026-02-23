# Release Process

The following process is used when publishing new releases to NuGet.org:

1. **Ensure the CI workflow is fully green**
    - Some of the integration tests are flaky and require re-running
    - Once the state of the branch is known to be good, a release can proceed
    - **The release workflow _does not_ run tests**

2. **Prepare release notes using Copilot CLI**
    - From a local clone of the repository, use Copilot CLI to invoke the `release-notes` skill
    - Provide the target commit (SHA, branch, or tag) when prompted, ensuring it's one from above where CI is known to be green
    - The skill will determine the version from `src/Directory.Build.props`, gather PRs, categorize changes, audit breaking changes, identify acknowledgements, and create a **draft** GitHub release
    - Review each section as the skill presents it and confirm or adjust as needed
    - After the draft release is created, review it on GitHub
    - Check the 'Set as a pre-release' checkbox if appropriate
    - Click 'Publish release'

3. **Monitor the Release workflow**
    - After publishing the release, a workflow will begin for producing the release's build artifacts and publishing the NuGet package to NuGet.org
    - If the job fails, troubleshoot and re-run the workflow as needed
    - Verify the package version becomes listed on at [https://nuget.org/packages/ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)

4. **Update the source to increment the version number**
    - The `release-notes` skill will offer to invoke the `bump-version` skill to create a pull request bumping the version
    - Alternatively, manually update [`/src/Directory.Build.Props`](../../src/Directory.Build.props) to bump the version to the next expected release version
