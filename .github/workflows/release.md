# Release Process

The following process is used when publishing new releases to NuGet.org:

1. **Ensure the CI workflow is fully green**
    - Some of the integration tests are flaky and require re-running
    - Once the state of the branch is known to be good, a release can proceed
    - **The release workflow _does not_ run tests**

2. **Create a new Release in GitHub**
    - Use the link on the repo home page to [Create a new release](https://github.com/modelcontextprotocol/csharp-sdk/releases/new)
    - Click the 'Choose a tag' dropdown button
        - Type the name using the `v{major}.{minor}.{patch}-{suffix}` pattern
        - Click 'Create new tag: ... on publish'
    - Click the 'Target' dropdown button
        - Choose the 'Recent Commits' tab
        - Select the commit to use for the release, ensuring it's one from above where CI is known to be green
    - Click the 'Previous tag' dropdown button
        - Choose the previous release to use for generating release notes
    - Click the 'Generate release notes button'
        - This will add release notes into the Release description
        - The generated release notes include what has changed and the list of new contributors
    - Verify the Release title
        - It will be populated to match the tag name to be created
        - This should be retained, using the release title format matching the `v{major}.{minor}.{patch}-{suffix}` format
    - Augment the Release description as desired
        - This content is presented used on GitHub and is not persisted into any artifacts
    - Check the 'Set as a pre-release' button under the release description if appropriate
    - Click 'Public release'

3. **Monitor the Release workflow**
    - After publishing the release, a workflow will begin for producing the release's build artifacts and publishing the NuGet package to NuGet.org
    - If the job fails, troubleshoot and re-run the workflow as needed
    - Verify the package version becomes listed on at [https://nuget.org/packages/ModelContextProtocol](https://nuget.org/packages/ModelContextProtocol)

4. **Update the source to increment the version number**
    - Immediately after publishing a new release, the [`/src/Directory.Build.Props`](../../src/Directory.Build.props) file needs to be updated to bump the version to the next expected release version
