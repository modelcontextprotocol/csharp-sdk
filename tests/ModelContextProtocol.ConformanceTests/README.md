# MCP Conformance Tests

This project contains integration tests that run the official Model Context Protocol (MCP) conformance test suite against the C# SDK's ConformanceServer implementation.

## Overview

The conformance tests verify that the C# MCP server implementation adheres to the MCP specification by running the official Node.js-based conformance test suite.

## Prerequisites

- .NET 10.0 SDK or later
- Node.js and npm (required to run the `@modelcontextprotocol/conformance` package)

## Running the Tests

Since these tests require Node.js/npm to be installed, they are marked as manual tests and excluded from the default test run.

### Run conformance tests explicitly

```bash
# Run only conformance tests
dotnet test tests/Conformance/ModelContextProtocol.ConformanceTests --filter 'Execution=Manual'

# Or run all manual tests across the solution
dotnet test --filter 'Execution=Manual'
```

### Skip conformance tests (default behavior)

```bash
# Normal test run excludes Manual tests
dotnet test --filter '(Execution!=Manual)'

# Or simply
dotnet test
```

## How It Works

1. **ClassInitialize** - Starts the ConformanceServer on port 3001 and waits for it to be ready
2. **Test Execution** - Runs `npx @modelcontextprotocol/conformance server --url http://localhost:3001`
3. **Result Reporting** - Parses the conformance test output and reports pass/fail to MSTest
4. **ClassCleanup** - Shuts down the ConformanceServer

## Troubleshooting

If the tests fail:

1. Ensure Node.js and npm are installed: `node --version && npm --version`
2. Check that port 3001 is not already in use
3. Review the test output for specific conformance test failures
4. The ConformanceServer logs are captured in the test output

## Implementation Details

- **Test Framework**: xUnit v3 with Microsoft.Testing.Platform
- **Server**: ASP.NET Core-based ConformanceServer with HTTP transport
- **Test Runner**: Uses `npx` to run the official MCP conformance test suite
- **Lifecycle**: Uses xUnit's `IAsyncLifetime` to manage server startup/shutdown per test class
