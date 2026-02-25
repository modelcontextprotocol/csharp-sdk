---
title: Capabilities
author: jeffhandley
description: How capability and protocol version negotiation works in MCP.
uid: capabilities
---

## Capabilities

MCP uses a [capability negotiation] mechanism during connection setup. Clients and servers exchange their supported capabilities so each side can adapt its behavior accordingly. Both sides should check the other's capabilities before using optional features.

[capability negotiation]: https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle#initialization

### Client capabilities

<xref:ModelContextProtocol.Protocol.ClientCapabilities> declares what features the client supports:

| Capability | Type | Description |
|-----------|------|-------------|
| `Roots` | <xref:ModelContextProtocol.Protocol.RootsCapability> | Client can provide filesystem root URIs |
| `Sampling` | <xref:ModelContextProtocol.Protocol.SamplingCapability> | Client can handle LLM sampling requests |
| `Elicitation` | <xref:ModelContextProtocol.Protocol.ElicitationCapability> | Client can present forms or URLs to the user |
| `Experimental` | `IDictionary<string, object>` | Experimental capabilities |

Configure client capabilities when creating an MCP client:

```csharp
var options = new McpClientOptions
{
    Capabilities = new ClientCapabilities
    {
        Roots = new RootsCapability { ListChanged = true },
        Sampling = new SamplingCapability(),
        Elicitation = new ElicitationCapability
        {
            Form = new FormElicitationCapability(),
            Url = new UrlElicitationCapability()
        }
    }
};

await using var client = await McpClient.CreateAsync(transport, options);
```

Handlers for each capability (roots, sampling, elicitation) are covered in their respective documentation pages.

### Server capabilities

<xref:ModelContextProtocol.Protocol.ServerCapabilities> declares what features the server supports:

| Capability | Type | Description |
|-----------|------|-------------|
| `Tools` | <xref:ModelContextProtocol.Protocol.ToolsCapability> | Server exposes callable tools |
| `Prompts` | <xref:ModelContextProtocol.Protocol.PromptsCapability> | Server exposes prompt templates |
| `Resources` | <xref:ModelContextProtocol.Protocol.ResourcesCapability> | Server exposes readable resources |
| `Logging` | <xref:ModelContextProtocol.Protocol.LoggingCapability> | Server can send log messages |
| `Completions` | <xref:ModelContextProtocol.Protocol.CompletionsCapability> | Server supports argument completions |
| `Experimental` | `IDictionary<string, object>` | Experimental capabilities |

Server capabilities are automatically inferred from the configured features. For example, registering tools with `.WithTools<T>()` automatically declares the tools capability.

### Checking capabilities

Before using an optional feature, check whether the other side declared the corresponding capability.

#### Checking server capabilities from the client

```csharp
await using var client = await McpClient.CreateAsync(transport);

// Check if the server supports tools
if (client.ServerCapabilities.Tools is not null)
{
    var tools = await client.ListToolsAsync();
}

// Check if the server supports resources with subscriptions
if (client.ServerCapabilities.Resources is { Subscribe: true })
{
    await client.SubscribeToResourceAsync("config://app/settings");
}

// Check if the server supports prompts with list-changed notifications
if (client.ServerCapabilities.Prompts is { ListChanged: true })
{
    mcpClient.RegisterNotificationHandler(
        NotificationMethods.PromptListChangedNotification,
        async (notification, ct) =>
        {
            var prompts = await mcpClient.ListPromptsAsync(cancellationToken: ct);
        });
}

// Check if the server supports logging
if (client.ServerCapabilities.Logging is not null)
{
    await client.SetLoggingLevelAsync(LoggingLevel.Info);
}

// Check if the server supports completions
if (client.ServerCapabilities.Completions is not null)
{
    var completions = await client.CompleteAsync(
        new PromptReference { Name = "my_prompt" },
        argumentName: "language",
        argumentValue: "py");
}
```

### Protocol version negotiation

During connection setup, the client and server negotiate a mutually supported MCP protocol version. After initialization, the negotiated version is available on both sides:

```csharp
// On the client
string? version = client.NegotiatedProtocolVersion;

// On the server (within a tool or handler)
string? version = server.NegotiatedProtocolVersion;
```

Version negotiation is handled automatically. If the client and server cannot agree on a compatible protocol version, the initialization fails with an error.
