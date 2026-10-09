# EverythingServer.Core

This is the core library containing all the shared MCP server handlers (tools, prompts, resources) used by both the HTTP and stdio implementations of the EverythingServer sample.

## What's Inside

This library contains:

- **Tools**: Various example tools demonstrating different MCP capabilities
  - `AddTool`: Simple addition operation
  - `AnnotatedMessageTool`: Returns annotated messages
  - `EchoTool`: Echoes input back to the client
  - `LongRunningTool`: Demonstrates long-running operations with progress reporting
  - `PrintEnvTool`: Prints environment variables
  - `SampleLlmTool`: Example LLM sampling integration
  - `TinyImageTool`: Returns image content

- **Prompts**: Example prompts showing argument handling
  - `SimplePromptType`: Basic prompt example
  - `ComplexPromptType`: Prompt with multiple arguments

- **Resources**: Example resources with subscriptions
  - `SimpleResourceType`: Dynamic resource with URI template matching

- **Background Services**: For managing subscriptions and logging
  - `SubscriptionMessageSender`: Sends periodic updates to subscribed resources
  - `LoggingUpdateMessageSender`: Sends periodic logging messages at different levels

- **Extension Method**: `AddEverythingMcpHandlers` to configure all handlers in one call

## Usage

Reference this project from your MCP server implementation (HTTP or stdio) and call the extension method:

```csharp
builder.Services
    .AddMcpServer()
    .WithHttpTransport() // or WithStdioServerTransport()
    .AddEverythingMcpHandlers(subscriptions);
```

See the `EverythingServer.Http` and `EverythingServer.Stdio` projects for complete examples.
