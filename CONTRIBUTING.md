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

### Writing Tests

The test projects include shared infrastructure in `tests/Common/Utils/` that most tests build on. Familiarize yourself with these helpers before writing new tests.

#### Test base classes

- **`LoggedTest`** — Base class that wires up `ILoggerFactory` with both `XunitLoggerProvider` (routes to test output) and `MockLoggerProvider` (captures logs for assertions). Inherit from this for any test that needs logging.
- **`ClientServerTestBase`** — Sets up an in-memory client/server pair connected via `Pipe` with proper async disposal. Override `ConfigureServices` to register tools, prompts, and resources, then call `CreateMcpClientForServer()` to get a connected client.
- **`KestrelInMemoryTest`** (ASP.NET Core tests) — Hosts an ASP.NET Core server with in-memory transport so HTTP/SSE tests run without allocating ports.

#### Choosing a transport

| Scenario | Transport | Why |
|---|---|---|
| Unit tests that only need DI | `WithStreamServerTransport(Stream.Null, Stream.Null)` | No threads blocked, no process spawned |
| Client/server interaction tests | `ClientServerTestBase` (uses `Pipe`) | Full bidirectional MCP, in-process |
| Client-only logic | `TestServerTransport` | In-memory mock that auto-responds to standard MCP requests |
| HTTP/SSE integration | `KestrelInMemoryTest` | Real HTTP stack, no network |
| External process tests | `StdioClientTransport` | Only when testing actual process lifecycle |

> **Do not** use `WithStdioServerTransport()` in unit tests. The stdio server transport reads from the test host process's standard input, which the test does not own and cannot close. This means the transport's background read loop can never terminate, permanently leaking a thread pool thread per test. Use `WithStreamServerTransport(Stream.Null, Stream.Null)` for tests that only need the DI container.

#### Resource management

- **Always `await using` the `ServiceProvider`** when MCP server services are registered — `McpServerImpl` only implements `IAsyncDisposable`, not `IDisposable`. A synchronous `using` will throw at runtime, and skipping disposal leaks transports and background threads.
- **Use `TestContext.Current.CancellationToken`** when calling async MCP methods so that xUnit can cancel the test on timeout rather than hanging.
- **Dispose clients and servers** explicitly. Prefer `await using var client = ...` over relying on finalizers. `ClientServerTestBase` handles this if you inherit from it.

#### Timeouts

Use `TestConstants.DefaultTimeout` (60 seconds) rather than hardcoded values. CI machines are often slower than developer workstations, and short timeouts cause flaky failures.

```csharp
// Good
using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
cts.CancelAfter(TestConstants.DefaultTimeout);
await client.CallToolAsync("my-tool", args, cts.Token);

// Bad — too short for CI
cts.CancelAfter(TimeSpan.FromSeconds(5));
```

#### Synchronization

Avoid `Task.Delay` for synchronization. Use explicit signaling primitives (`TaskCompletionSource`, `SemaphoreSlim`, `Channel`) so tests don't depend on timing. If a producer/consumer test writes events for a streaming reader, use a `TaskCompletionSource` to confirm the reader is active before writing.

#### Background logging

`ITestOutputHelper.WriteLine` throws after the test method returns. Background threads (process event handlers, async continuations) can outlive the test. This manifests as unhandled exceptions that crash the test host. Two mitigations:

1. **`XunitLoggerProvider`** already catches these exceptions. Route logging through `LoggedTest.LoggerFactory` rather than calling `ITestOutputHelper` directly from callbacks.
2. **If you must call `ITestOutputHelper` from an event handler**, wrap it in a try/catch:
   ```csharp
   process.ErrorDataReceived += (s, e) =>
   {
       try { testOutputHelper.WriteLine(e.Data); }
       catch (InvalidOperationException) { }
   };
   ```

#### Parallelism

Tests run in parallel by default. If a test class touches global state (e.g., `ActivitySource` listeners in diagnostics tests), apply `[Collection(nameof(DisableParallelization))]` to run it sequentially.

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
