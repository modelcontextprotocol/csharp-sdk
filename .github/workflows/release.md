# Release Process

The following process is used when publishing new releases to NuGet.org:

1. Ensure the CI workflow is fully green
    - Some of the integration tests are flaky and require re-running.
    - Once the state of the branch is known to be good, a release can proceed.

2. Identify the commit to be used for the release
    - A tag can be created during the Release process, but it can optionally be created ahead of time.
    - If created ahead of time, it should have the format of `v{major}.{minor}.{patch}-{suffix}`. Example: `v0.1.0-preview.2`, where the version prefix and suffix are used from `/src/Directory.Build.props`.
    - A fully green CI run should exist for the commit.

3.
