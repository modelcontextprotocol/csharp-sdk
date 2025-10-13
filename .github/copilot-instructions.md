# Copilot Instructions for MCP C# SDK

This repository contains the official C# SDK for the Model Context Protocol (MCP), enabling .NET applications to implement and interact with MCP clients and servers.

## Project Overview

The SDK consists of three main packages:
- **ModelContextProtocol.Core** - Client and low-level server APIs with minimal dependencies
- **ModelContextProtocol** - The main package with hosting and dependency injection extensions and which references ModelContextProtocol.Core
- **ModelContextProtocol.AspNetCore** - HTTP-based MCP server implementations for ASP.NET Core, referencing ModelContextProtocol

## C# Coding Standards

### Language Features
- Use **file-scoped namespaces** for all C# files
- Enable **implicit usings** and **nullable reference types**
- Use **preview language features** (LangVersion: preview)
- Treat warnings as errors

### Code Style
- Follow the conventions in `.editorconfig`
- Use clear, descriptive XML documentation comments for public APIs
- Follow async/await patterns consistently
- Use file-scoped namespaces: `namespace ModelContextProtocol.Client;`

### Naming Conventions
- Use `McpClient`, `McpServer`, `McpSession` for MCP-related classes (capitalize MCP)
- Prefix MCP-specific types with `Mcp` (e.g., `McpException`, `McpEndpoint`)
- Use descriptive names for parameters with `[Description("...")]` attributes when exposing to MCP

## Architecture Patterns

### Dependency Injection
- Use Microsoft.Extensions.DependencyInjection patterns
- Register services with `.AddMcpServer()` and `.AddMcpClient()` extension methods
- Support both builder patterns and options configuration

### JSON Serialization
- Use `System.Text.Json` for all JSON operations
- Use `McpJsonUtilities.DefaultOptions` for consistent serialization
- Support source generation for Native AOT compatibility
- Set `JsonIgnoreCondition.WhenWritingNull` for optional properties

### Async Patterns
- All I/O operations should be async
- Use `ValueTask<T>` for hot paths that may complete synchronously
- Always accept `CancellationToken` parameters for async operations
- Name parameters consistently: `cancellationToken`

### MCP Protocol
- Follow the MCP specification at https://spec.modelcontextprotocol.io/ (https://github.com/modelcontextprotocol/modelcontextprotocol/tree/main/docs/specification)
- Use JSON-RPC 2.0 for message transport
- Support all standard MCP capabilities (e.g. tools, prompts, resources, sampling)
- Implement proper error handling with `McpException` and `McpErrorCode`

## Testing

### Test Organization
- Unit tests in `tests/ModelContextProtocol.Tests`
- Integration tests in `tests/ModelContextProtocol.AspNetCore.Tests`
- Test helpers in `tests/Common`
- Filter manual tests with `[Trait("Execution", "Manual")]`

### Test Infrastructure
- Use xUnit for all tests
- Run tests with: `dotnet test --filter '(Execution!=Manual)'`
- Tests should be isolated and not depend on external services (except manual tests)

## Build and Development

### Build Commands
- **Restore**: `dotnet restore` or `make restore`
- **Build**: `dotnet build` or `make build`
- **Test**: `dotnet test` or `make test`
- **Clean**: `dotnet clean` or `make clean`

### SDK Requirements
- The project uses .NET SDK preview versions
- Target frameworks: .NET 8.0, .NET 9.0, .NET Standard 2.0
- Support Native AOT compilation

### Project Structure
- Source code: `src/`
- Tests: `tests/`
- Samples: `samples/`
- Documentation: `docs/`
- Build artifacts: `artifacts/` (not committed)

## Common Patterns

### MCP Server Tools
Tools are exposed using attributes:
```csharp
[McpServerToolType]
public class MyTools
{
    [McpServerTool, Description("Tool description")]
    public static async Task<string> MyTool(
        [Description("Parameter description")] string param,
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

### MCP Server Prompts
Prompts are exposed similarly:
```csharp
[McpServerPromptType]
public static class MyPrompts
{
    [McpServerPrompt, Description("Prompt description")]
    public static ChatMessage MyPrompt([Description("Parameter description")] string content) =>
        new(ChatRole.User, $"Prompt template: {content}");
}
```

### Client Usage
```csharp
var client = await McpClient.CreateAsync(
    new StdioClientTransport(new() { Command = "...", Arguments = [...] }),
    clientOptions: new() { /* ... */ },
    loggerFactory: loggerFactory);

var tools = await client.ListToolsAsync();
var result = await client.CallToolAsync("tool-name", arguments, cancellationToken);
```

## OpenTelemetry Integration

- The SDK includes built-in observability support
- Use ActivitySource name: `"Experimental.ModelContextProtocol"`
- Use Meter name: `"Experimental.ModelContextProtocol"`
- Export traces and metrics using OTLP when appropriate

## Documentation

- API documentation is generated using DocFX
- Conceptual documentation is in `docs/concepts/`
- Keep README files up to date in package directories
- Use `///` XML comments for all public APIs
- Include `<remarks>` sections for detailed explanations

## Security

- Never commit secrets or API keys
- Use environment variables for sensitive configuration
- Support authentication mechanisms (OAuth, API keys)
- Validate all user inputs
- Follow secure coding practices per SECURITY.md

## Additional Notes

- This is a preview SDK; breaking changes may occur
- Follow the Model Context Protocol specification
- Integrate with Microsoft.Extensions.AI patterns where applicable
- Support both stdio and HTTP transports
- Maintain compatibility with the broader MCP ecosystem
