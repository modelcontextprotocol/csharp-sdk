# Copilot Instructions for MCP C# SDK

This repository contains the official C# SDK for the Model Context Protocol (MCP), enabling .NET applications to implement and interact with MCP clients and servers.

## Critical: Always Build and Test

**ALWAYS build and run tests before declaring any task complete or making a pull request.**

When making code changes:
1. **Build first**: Run `dotnet build` to ensure the code compiles without errors
2. **Run tests**: Execute `dotnet test` to verify all tests pass
3. **Fix issues**: Address any build errors or test failures before proceeding
4. **Verify iteratively**: Build and test frequently during development, not just at the end
5. **Check warnings**: Treat warnings as errors - the build is configured with `TreatWarningsAsErrors=true`

**Never skip these steps.** Even small changes can have unexpected impacts. A passing build and test suite is the minimum bar for any code change.

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
- Use `System.Text.Json` exclusively for all JSON operations
- Use `McpJsonUtilities.DefaultOptions` for consistent serialization settings across the SDK
- Support source generation for Native AOT compatibility via `McpJsonUtilities` source generators
- Set `JsonIgnoreCondition.WhenWritingNull` for optional properties to minimize payload size
- Use `JsonSerializerDefaults.Web` for camelCase property naming
- Protocol types are decorated with `[JsonSerializable]` attributes for AOT support
- Custom converters: `CustomizableJsonStringEnumConverter` for flexible enum serialization

### Async Patterns
- All I/O operations should be async
- Use `ValueTask<T>` for hot paths that may complete synchronously
- Always accept `CancellationToken` parameters for async operations
- Name parameters consistently: `cancellationToken`

### MCP Protocol
- Follow the MCP specification at https://spec.modelcontextprotocol.io/ ([specification docs](https://github.com/modelcontextprotocol/modelcontextprotocol/tree/main/docs/specification))
- Use JSON-RPC 2.0 for message transport
- Support all standard MCP capabilities (e.g. tools, prompts, resources, sampling)
- Implement proper error handling with `McpException` and `McpErrorCode`

### Error Handling
- Throw `McpException` for MCP protocol-level errors with appropriate `McpErrorCode`
- Use standard error codes: `InvalidRequest`, `MethodNotFound`, `InvalidParams`, `InternalError`
- Let domain exceptions bubble up and convert to `InternalError` at transport boundary
- Include detailed error messages in exception `Message` property for debugging
- Errors are automatically converted to JSON-RPC error responses by the server infrastructure

## Testing

### Test Organization
- Unit tests in `tests/ModelContextProtocol.Tests` for core functionality
- Integration tests in `tests/ModelContextProtocol.AspNetCore.Tests` for HTTP/SSE transports
- Shared test utilities in `tests/Common/Utils/`
- Test servers in `tests/ModelContextProtocol.Test*Server/` for integration scenarios
- Filter manual tests with `[Trait("Execution", "Manual")]` - these require external dependencies

### Test Infrastructure and Helpers
- **LoggedTest**: Base class for tests that need logging output captured to xUnit test output
  - Provides `ILoggerFactory` and `ITestOutputHelper` for test logging
  - Use when debugging or when tests need to verify log output
- **TestServerTransport**: In-memory transport for testing client-server interactions without network I/O
- **MockLoggerProvider**: For capturing and asserting on log messages
- **XunitLoggerProvider**: Routes `ILogger` output to xUnit's `ITestOutputHelper`
- **KestrelInMemoryTransport** (AspNetCore.Tests): In-memory Kestrel connection for HTTP transport testing without network stack

### Test Best Practices
- Inherit from `LoggedTest` for tests needing logging infrastructure
- Use `TestServerTransport` for in-memory client-server testing
- Mock external dependencies (filesystem, HTTP clients) rather than calling real services
- Use `CancellationTokenSource` with timeouts to prevent hanging tests
- Dispose resources properly (servers, clients, transports) using `IDisposable` or `await using`
- Run tests with: `dotnet test --filter '(Execution!=Manual)'`

## Build and Development

### Build Commands
- **Restore**: `dotnet restore`
- **Build**: `dotnet build`
- **Test**: `dotnet test`
- **Clean**: `dotnet clean`

### Development Workflow
**Critical**: Always follow this workflow when making changes:
1. Make code changes
2. Build immediately: `dotnet build` - fix any compilation errors
3. Run tests: `dotnet test` - fix any test failures
4. Repeat steps 1-3 iteratively as you develop
5. Only after successful build and tests should you consider the change complete

Do not skip or defer building and testing. These are mandatory steps for every code change, no matter how small.

### SDK Requirements
- The repo currently requires the .NET SDK 10.0 to build and run tests.
- Target frameworks: .NET 10.0, .NET 9.0, .NET 8.0, .NET Standard 2.0
- Support Native AOT compilation

### Project Structure
- Source code: `src/`
- Tests: `tests/`
- Samples: `samples/`
- Documentation: `docs/`
- Build artifacts: `artifacts/` (not committed)

## Key Types and Architectural Layers

The SDK is organized into distinct architectural layers, each with specific responsibilities:

### Protocol Layer (DTO Types)
- Located in `ModelContextProtocol.Core/Protocol/`
- Contains Data Transfer Objects (DTOs) for the MCP specification
- All protocol types follow JSON-RPC 2.0 conventions
- Key types:
  - **JsonRpcMessage** (abstract base): Represents any JSON-RPC message (request, response, notification, error)
  - **JsonRpcRequest**, **JsonRpcResponse**, **JsonRpcNotification**: Concrete message types
  - **Tool**, **Prompt**, **Resource**: MCP primitive definitions
  - **CallToolRequestParams**, **GetPromptRequestParams**, **ReadResourceRequestParams**: Request parameter types
  - **ClientCapabilities**, **ServerCapabilities**: Capability negotiation types
  - **Implementation**: Server/client identification metadata

### JSON-RPC Implementation
- Built-in JSON-RPC 2.0 implementation for MCP communication
- **JsonRpcMessage.Converter**: Polymorphic converter that deserializes messages into correct types based on structure
- **JsonRpcMessageContext**: Transport-specific metadata (transport reference, execution context, authenticated user)
- Message routing handled automatically by session implementations
- Error responses generated via **McpException** with **McpErrorCode** enumeration

### Transport Abstraction
- **ITransport**: Core abstraction for bidirectional communication
  - Provides `MessageReader` (ChannelReader) for incoming messages
  - `SendMessageAsync()` for outgoing messages
  - `SessionId` property for multi-session scenarios
- **IClientTransport**: Client-side abstraction that establishes connections and returns ITransport
- **TransportBase**: Base class for transport implementations with common functionality

### Transport Implementations
Two primary transport implementations with different invariants:

1. **Stdio-based transports** (`StdioServerTransport`, `StdioClientTransport`):
   - Single-session, process-bound communication
   - Uses standard input/output streams
   - No session IDs (returns null)
   - Automatic lifecycle tied to process
   
2. **HTTP-based transports**:
   - **SseResponseStreamTransport**: Server-Sent Events for server-to-client streaming
     - Unidirectional (server → client) event stream
     - Client posts messages to separate endpoint (e.g., `/message`)
     - Supports multiple concurrent sessions via SessionId
   - **StreamableHttpServerTransport**: Bidirectional HTTP with streaming
     - Request/response model with streamed progress updates
     - Session management for concurrent connections

### Session Layer
- **McpSession** (abstract base): Core bidirectional communication for clients and servers
  - Manages JSON-RPC request/response correlation
  - Handles notification routing
  - Provides `SendRequestAsync<TResult>()`, `SendNotificationAsync()`, `RegisterNotificationHandler()`
  - Properties: `SessionId`, `NegotiatedProtocolVersion`
  
- **McpClient** (extends McpSession): Client-side MCP implementation
  - Connects to servers via `CreateAsync(IClientTransport)`
  - Exposes `ServerCapabilities`, `ServerInfo`, `ServerInstructions`
  - Methods: `ListToolsAsync()`, `CallToolAsync()`, `ListPromptsAsync()`, `GetPromptAsync()`, etc.
  
- **McpServer** (extends McpSession): Server-side MCP implementation
  - Configured via `McpServerOptions` and `IMcpServerBuilder`
  - Primitives registered as services: `McpServerTool`, `McpServerPrompt`, `McpServerResource`
  - Handles incoming requests through `McpServer.Methods.cs`
  - Supports filters via `McpRequestFilter` for cross-cutting concerns

### Serialization Architecture
- **McpJsonUtilities.DefaultOptions**: Singleton JsonSerializerOptions for all MCP types
  - Hardcoded to use source-generated serialization for JSON-RPC messages (Native AOT compatible)
  - Source generation defined in `McpJsonUtilities` via `[JsonSerializable]` attributes
  - Includes Microsoft.Extensions.AI types via chained TypeInfoResolver
  
- **User-defined types** (tool parameters, return values):
  - Accept custom `JsonSerializerOptions` via `McpServerToolCreateOptions.SerializerOptions`
  - Default to `McpJsonUtilities.DefaultOptions` if not specified
  - Can use reflection-based serialization or custom source generators
  
- **Enum handling**: `CustomizableJsonStringEnumConverter` for flexible enum serialization

## Architecture and Design Patterns

### Server Implementation Architecture
- **McpServer** is the core server implementation in `ModelContextProtocol.Core/Server/`
- **IMcpServerBuilder** pattern provides fluent API for configuring servers via DI
- Server primitives (tools, prompts, resources) are discovered via reflection using attributes
- Support both attribute-based registration (`WithTools<T>()`) and instance-based (`WithTools(target)`)
- Use `McpServerFactory` to create server instances with configured options

### Tool/Prompt/Resource Discovery
- Tools, prompts, and resources use attribute-based discovery: `[McpServerTool]`, `[McpServerPrompt]`, `[McpServerResource]`
- Type-level attributes (`[McpServerToolType]`, etc.) mark classes containing server primitives
- Discovery supports both static and instance methods (public and non-public)
- For Native AOT compatibility, use generic `WithTools<T>()` methods instead of reflection-based variants
- `AIFunctionMcpServerTool`, `AIFunctionMcpServerPrompt`, and `AIFunctionMcpServerResource` wrap `AIFunction` for integration with Microsoft.Extensions.AI

### Request Processing Pipeline
- Requests flow through `McpServer.Methods.cs` which handles JSON-RPC message routing
- Use `McpRequestFilter` for cross-cutting concerns (logging, auth, validation)
- `RequestContext` provides access to current request state and services
- `RequestServiceProvider` enables scoped dependency injection per request
- Filters can short-circuit request processing or transform requests/responses

### Transport Layer Abstraction
- Transport implementations handle message serialization and connection management
- Core transports: `StdioServerTransport`, `StreamServerTransport`, `SseResponseStreamTransport`, `StreamableHttpServerTransport`
- Transports must implement bidirectional JSON-RPC message exchange
- SSE (Server-Sent Events) transport for unidirectional server→client streaming
- Streamable HTTP for request/response with streamed progress updates

## Implementation Patterns

### Tool Implementation
Tools are methods marked with `[McpServerTool]`:
```csharp
[McpServerToolType]
public class MyTools
{
    [McpServerTool, Description("Tool description")]
    public static async Task<string> MyTool(
        [Description("Parameter description")] string param,
        CancellationToken cancellationToken)
    {
        // Implementation - use Description attributes for parameter documentation
        // Return string, TextContent, ImageContent, EmbeddedResource, or arrays of these
    }
}
```
- Tools support dependency injection in constructors for instance methods
- Parameters are automatically deserialized from JSON using `System.Text.Json`
- Use `[Description]` attributes on parameters to generate tool schemas
- Return types: `string`, `TextContent`, `ImageContent`, `EmbeddedResource`, or collections of content types

### Prompt Implementation
Prompts return `ChatMessage` or arrays thereof:
```csharp
[McpServerPromptType]
public static class MyPrompts
{
    [McpServerPrompt, Description("Prompt description")]
    public static ChatMessage MyPrompt([Description("Parameter description")] string content) =>
        new(ChatRole.User, $"Prompt template: {content}");
}
```
- Prompts can accept arguments to customize generated messages
- Return single `ChatMessage` or `ChatMessage[]` for multi-turn prompts
- Use `ChatRole.User`, `ChatRole.Assistant`, or `ChatRole.System` appropriately

### Resource Implementation
Resources provide access to data with URI templates:
```csharp
[McpServerResourceType]
public class MyResources
{
    [McpServerResource("file:///{path}"), Description("Reads file content")]
    public static async Task<string> ReadFile(string path, CancellationToken cancellationToken)
    {
        // Resource URI matching uses UriTemplate syntax
        // Extract parameters from URI and return content
    }
}
```
- Use URI templates to define resource paths with parameters
- Resources support subscription for dynamic content updates
- Return content types similar to tools

### Filters and Middleware
Implement `McpRequestFilter` for request/response interception:
```csharp
public class LoggingFilter : McpRequestFilter
{
    public override async ValueTask InvokeAsync(RequestContext context, Func<ValueTask> next)
    {
        // Pre-processing
        await next(); // Call next filter or handler
        // Post-processing
    }
}
```
- Filters execute in registration order
- Can short-circuit by not calling `next()`
- Access request context, services, and can modify responses
- Use for cross-cutting concerns: logging, auth, validation, caching

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
