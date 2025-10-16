# Running Tests

## Manual Tests

Some tests in this repository are marked with the `[Trait("Execution", "Manual")]` attribute. These tests require external dependencies or network connectivity and are not run by default in CI/CD pipelines.

### Microsoft Learn MCP Server Tests

The `MicrosoftLearnMcpServerTests` class contains integration tests that connect to the Microsoft Learn MCP server at `https://learn.microsoft.com/api/mcp` using Streamable HTTP transport.

These tests:
- Require network connectivity to Microsoft Learn services
- Test real-world integration with a production MCP server
- Validate the Streamable HTTP transport implementation

#### Running the Microsoft Learn tests

To run these tests, use the following command:

```bash
dotnet test --filter "(FullyQualifiedName~MicrosoftLearnMcpServerTests)"
```

Or to run all manual tests:

```bash
dotnet test --filter "(Execution=Manual)"
```

To exclude manual tests from a test run:

```bash
dotnet test --filter "(Execution!=Manual)"
```

### Requirements

- .NET 10.0 SDK (as specified in `global.json`)
- Internet connectivity
- Access to https://learn.microsoft.com/api/mcp (ensure no firewall restrictions)
