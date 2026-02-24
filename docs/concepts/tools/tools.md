---
title: Tools
author: jeffhandley
description: How to implement and consume MCP tools that return text, images, audio, and embedded resources.
uid: tools
---

## Tools

MCP [tools] allow servers to expose callable functions to clients. Tools are the primary mechanism for LLMs to take action through MCP&mdash;they enable everything from querying databases to calling web APIs.

[tools]: https://modelcontextprotocol.io/specification/2025-11-25/server/tools

This document covers tool content types, change notifications, and schema generation.

### Defining tools on the server

Tools are defined as methods marked with the <xref:ModelContextProtocol.Server.McpServerToolAttribute> attribute within a class marked with <xref:ModelContextProtocol.Server.McpServerToolTypeAttribute>. Parameters are automatically deserialized from JSON and documented using `[Description]` attributes.

```csharp
[McpServerToolType]
public class MyTools
{
    [McpServerTool, Description("Echoes the input message back")]
    public static string Echo([Description("The message to echo")] string message)
        => $"Echo: {message}";
}
```

Register the tool type when building the server:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<MyTools>();
```

### Content types

Tools can return various content types. The simplest is a `string`, which is automatically wrapped in a <xref:ModelContextProtocol.Protocol.TextContentBlock>. For richer content, tools can return one or more <xref:ModelContextProtocol.Protocol.ContentBlock> instances.

#### Text content

Return a `string` or a <xref:ModelContextProtocol.Protocol.TextContentBlock> directly:

```csharp
[McpServerTool, Description("Returns a greeting")]
public static string Greet(string name) => $"Hello, {name}!";
```

#### Image content

Return an <xref:ModelContextProtocol.Protocol.ImageContentBlock> with base64-encoded image data and a MIME type.
Use the <xref:ModelContextProtocol.Protocol.ImageContentBlock.FromBytes*> factory method or construct the block directly:

```csharp
[McpServerTool, Description("Returns a generated image")]
public static ImageContentBlock GenerateImage()
{
    byte[] pngBytes = CreateImage(); // your image generation logic
    return ImageContentBlock.FromBytes(pngBytes, "image/png");
}
```

#### Audio content

Return an <xref:ModelContextProtocol.Protocol.AudioContentBlock> with base64-encoded audio data and a MIME type.
The <xref:ModelContextProtocol.Protocol.AudioContentBlock.FromBytes*> factory method encodes the raw bytes automatically:

```csharp
[McpServerTool, Description("Returns a synthesized audio clip")]
public static AudioContentBlock Synthesize(string text)
{
    byte[] wavBytes = TextToSpeech(text); // your audio synthesis logic
    return AudioContentBlock.FromBytes(wavBytes, "audio/wav");
}
```

Supported audio MIME types include `audio/wav`, `audio/mp3`, `audio/ogg`, and others depending on what the client can handle.

#### Embedded resources

Return an <xref:ModelContextProtocol.Protocol.EmbeddedResourceBlock> to embed a resource directly in a tool result.
The resource can contain either text or binary data through <xref:ModelContextProtocol.Protocol.TextResourceContents> or <xref:ModelContextProtocol.Protocol.BlobResourceContents>:

```csharp
[McpServerTool, Description("Returns a file as an embedded resource")]
public static EmbeddedResourceBlock ReadFile(string path)
{
    return new EmbeddedResourceBlock
    {
        Resource = new TextResourceContents
        {
            Uri = $"file:///{path}",
            MimeType = "text/plain",
            Text = File.ReadAllText(path)
        }
    };
}
```

For binary resources, use <xref:ModelContextProtocol.Protocol.BlobResourceContents>:

```csharp
[McpServerTool, Description("Returns a binary file as an embedded resource")]
public static EmbeddedResourceBlock ReadBinaryFile(string path)
{
    return new EmbeddedResourceBlock
    {
        Resource = BlobResourceContents.FromBytes(
            File.ReadAllBytes(path), $"file:///{path}", "application/octet-stream")
    };
}
```

#### Mixed content

Tools can return multiple content blocks by returning `IEnumerable<ContentBlock>`:

```csharp
[McpServerTool, Description("Returns text and an image")]
public static IEnumerable<ContentBlock> DescribeImage()
{
    byte[] imageBytes = GetImage();
    return
    [
        new TextContentBlock { Text = "Here is the generated image:" },
        ImageContentBlock.FromBytes(imageBytes, "image/png"),
        new TextContentBlock { Text = "The image shows a landscape." }
    ];
}
```

#### Content annotations

Any content block can include <xref:ModelContextProtocol.Protocol.Annotations> to provide hints about the intended audience and priority:

```csharp
new TextContentBlock
{
    Text = "Detailed debug information",
    Annotations = new Annotations
    {
        Audience = [Role.Assistant], // Only for the LLM, not the user
        Priority = 0.3f             // Low priority (0.0 to 1.0)
    }
}
```

### Consuming tools on the client

Clients can discover and call tools using <xref:ModelContextProtocol.Client.McpClient>:

```csharp
// List available tools
IList<McpClientTool> tools = await client.ListToolsAsync();

foreach (var tool in tools)
{
    Console.WriteLine($"{tool.Name}: {tool.Description}");
}

// Call a tool
CallToolResult result = await client.CallToolAsync(
    "echo",
    new Dictionary<string, object?> { ["message"] = "Hello!" });

// Process the result content blocks
foreach (var content in result.Content)
{
    switch (content)
    {
        case TextContentBlock text:
            Console.WriteLine(text.Text);
            break;
        case ImageContentBlock image:
            // image.DecodedData contains the raw bytes
            File.WriteAllBytes("output.png", image.DecodedData.ToArray());
            break;
        case AudioContentBlock audio:
            // audio.DecodedData contains the raw bytes
            File.WriteAllBytes("output.wav", audio.DecodedData.ToArray());
            break;
        case EmbeddedResourceBlock resource:
            if (resource.Resource is TextResourceContents textResource)
                Console.WriteLine(textResource.Text);
            break;
    }
}
```

### Error handling

Tool errors in MCP are distinct from protocol errors. When a tool encounters an error during execution, the error is reported inside the <xref:ModelContextProtocol.Protocol.CallToolResult> with <xref:ModelContextProtocol.Protocol.CallToolResult.IsError> set to `true`, rather than as a protocol-level exception. This allows the LLM to see the error and potentially recover.

#### Automatic exception handling

When a tool method throws an exception, the server automatically catches it and returns a `CallToolResult` with `IsError = true`. <xref:ModelContextProtocol.McpProtocolException> is re-thrown as a JSON-RPC error, and <xref:System.OperationCanceledException> is re-thrown when the cancellation token was triggered. All other exceptions are returned as tool error results. For <xref:ModelContextProtocol.McpException> subclasses, the exception message is included in the error text; for other exceptions, a generic message is returned to avoid leaking internal details.

```csharp
[McpServerTool, Description("Divides two numbers")]
public static double Divide(double a, double b)
{
    if (b == 0)
    {
        // ArgumentException is not an McpException, so the client receives a generic message:
        // "An error occurred invoking 'divide'."
        throw new ArgumentException("Cannot divide by zero");
    }

    return a / b;
}
```

#### Protocol errors

Throw <xref:ModelContextProtocol.McpProtocolException> to signal a protocol-level error (e.g., invalid parameters or unknown tool). These exceptions propagate as JSON-RPC error responses rather than tool error results:

```csharp
[McpServerTool, Description("Processes the input")]
public static string Process(string input)
{
    if (string.IsNullOrEmpty(input))
    {
        // Propagates as a JSON-RPC error with code -32602 (InvalidParams)
        // and message "Missing required input"
        throw new McpProtocolException("Missing required input", McpErrorCode.InvalidParams);
    }

    return $"Processed: {input}";
}
```

#### Checking for errors on the client

On the client side, inspect the <xref:ModelContextProtocol.Protocol.CallToolResult.IsError> property after calling a tool:

```csharp
CallToolResult result = await client.CallToolAsync("divide", new Dictionary<string, object?>
{
    ["a"] = 10,
    ["b"] = 0
});

if (result.IsError is true)
{
    // Prints: "Tool error: An error occurred invoking 'divide'."
    Console.WriteLine($"Tool error: {result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text}");
}
```

### Tool list change notifications

Servers can dynamically add, remove, or modify tools at runtime. When the tool list changes, the server notifies connected clients so they can refresh their tool list.

#### Sending notifications from the server

Inject <xref:ModelContextProtocol.Server.McpServer> and call the notification method after modifying the tool list:

```csharp
// After adding or removing tools dynamically
await server.SendNotificationAsync(
    NotificationMethods.ToolListChangedNotification,
    new ToolListChangedNotificationParams());
```

#### Handling notifications on the client

Register a notification handler on the client to respond to tool list changes:

```csharp
mcpClient.RegisterNotificationHandler(
    NotificationMethods.ToolListChangedNotification,
    async (notification, cancellationToken) =>
    {
        // Refresh the tool list
        var updatedTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"Tool list updated. {updatedTools.Count} tools available.");
    });
```

### JSON Schema generation

Tool parameters are described using [JSON Schema 2020-12]. JSON schemas are automatically generated from .NET method signatures when the `[McpServerTool]` attribute is applied. Parameter types are mapped to JSON Schema types:

[JSON Schema 2020-12]: https://json-schema.org/specification

| .NET Type | JSON Schema Type |
|-----------|-----------------|
| `string` | `string` |
| `int`, `long` | `integer` |
| `float`, `double` | `number` |
| `bool` | `boolean` |
| `enum` | `string` with `enum` values |
| Complex types | `object` with `properties` |

Use `[Description]` attributes on parameters to populate the `description` field in the generated schema. This helps LLMs understand what each parameter expects.

```csharp
[McpServerTool, Description("Searches for items")]
public static string Search(
    [Description("The search query string")] string query,
    [Description("Maximum results to return (1-100)")] int maxResults = 10)
{
    // Schema will include descriptions and default value for maxResults
}
```
