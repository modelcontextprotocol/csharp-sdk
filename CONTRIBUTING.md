# Contributing to MCP C# SDK

Thank you for your interest in contributing to the Model Context Protocol (MCP) C# SDK! This document provides guidelines and instructions for contributing to the project.

One of the easiest ways to contribute is to participate in discussions on GitHub issues. You can also contribute by submitting pull requests with code changes.

Also see the [overall MCP communication guidelines in our docs](https://modelcontextprotocol.io/community/communication), which explains how and where discussions about changes happen.

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code.

## Bugs and feature requests

> [!IMPORTANT]
> **If you want to report a security-related issue, please see the [Reporting security issues](SECURITY.md#reporting-security-issues) section of SECURITY.md.**

Before reporting a new issue, try to find an existing issue if one already exists. If it already exists, upvote (👍) it. Also, consider adding a comment with your unique scenarios and requirements related to that issue.  Upvotes and clear details on the issue's impact help us prioritize the most important issues to be worked on sooner rather than later. If you can't find one, that's okay, we'd rather get a duplicate report than none.

If you can't find an existing issue, please [open a new issue on GitHub](https://github.com/modelcontextprotocol/csharp-sdk/issues).

## Prerequisites

Before you begin, ensure you have the following installed:

- **.NET SDK 10.0 or later** - Required to build and test the project
  - Download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
  - Verify installation: `dotnet --version`

The dev container configuration in this repository includes all the necessary tools and SDKs to get started quickly.

## Building the Project

From the root directory of the repository, run:

```bash
dotnet build
```

This builds all projects in the solution with warnings treated as errors.

## Running Tests

### Run All Tests

From the root directory, run:

```bash
dotnet test
```

Some tests require Docker to be installed and running locally. If Docker is not available, those tests will be skipped.

Some tests require credentials for external services. When these are not available, those tests will be skipped.

Use the following environment variables to provide credentials for external services:

- AI:OpenAI:ApiKey - OpenAI API Key

### Run Tests for a Specific Project

```bash
dotnet test tests/ModelContextProtocol.Tests/
```

Tools like Visual Studio, JetBrains Rider, and VS Code also provide integrated test runners that can be used to run and debug individual tests.

### Building the Documentation

This project uses [DocFX](https://dotnet.github.io/docfx/) to generate its conceptual and reference documentation.

To view the documentation locally, run the following command from the root directory:

```bash
make serve-docs
```

Then open your browser and navigate to `http://localhost:8080`.

## Submitting Pull Requests

We are always happy to see PRs from community members both for bug fixes as well as new features.
Here are a few simple rules to follow when you prepare to contribute to our codebase:

### Finding an issue to work on

Issues that are good candidates for first-time contributors are marked with the `good first issue` label.
Those do not require too much familiarity with the framework and are more novice-friendly.

If you want to contribute a change that is not covered by an existing issue, first open an issue with a description of the change you would like to make and the problem it solves so it can be discussed before a pull request is submitted.

Assign yourself to the issue so others know you are working on it.

### Before writing code

For all but the smallest changes, it's a good idea to create a design document or at least a high-level description of your approach and share it in the issue for feedback before you start coding. This helps ensure that your approach aligns with the project's goals and avoids wasted effort.

### Before submitting the pull request

Before submitting a pull request, make sure that it checks the following requirements:

- The code follows the repository's style guidelines
- Tests are included for new features or bug fixes
- All existing and new tests pass locally
- Appropriate error handling has been added
- Documentation has been updated as needed

When submitting the pull request, provide a clear description of the changes made and reference the issue it addresses.

### During pull request review

A project maintainer will review your pull request and provide feedback.

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
