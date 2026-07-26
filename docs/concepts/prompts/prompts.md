---
title: Prompts
author: jeffhandley
description: How to implement and consume MCP prompts that return text, images, and embedded resources.
uid: prompts
---

## Prompts

MCP [prompts] allow servers to expose reusable prompt templates to clients. Prompts provide a way for servers to define structured messages that can be parameterized and composed into conversations.

[prompts]: https://modelcontextprotocol.io/specification/2025-11-25/server/prompts

This document covers implementing prompts on the server, consuming them from the client, rich content types, and change notifications.

### Defining prompts on the server

Prompts can be defined in several ways:

- Using the <xref:ModelContextProtocol.Server.McpServerPromptAttribute> attribute on methods within a class marked with <xref:ModelContextProtocol.Server.McpServerPromptTypeAttribute>
- Using <xref:ModelContextProtocol.Server.McpServerPrompt.Create*> factory methods from a delegate, `MethodInfo`, or `AIFunction`
- Deriving from <xref:ModelContextProtocol.Server.McpServerPrompt> or <xref:ModelContextProtocol.Server.DelegatingMcpServerPrompt>
- Implementing a custom <xref:ModelContextProtocol.Server.McpRequestHandler`2> via <xref:ModelContextProtocol.Server.McpServerHandlers>
- Implementing a low-level <xref:ModelContextProtocol.Server.McpRequestFilter`2>

The attribute-based approach is the most common and is shown throughout this document. Prompts can return `ChatMessage` instances for simple text/image content, or <xref:ModelContextProtocol.Protocol.PromptMessage> instances when protocol-specific content types like <xref:ModelContextProtocol.Protocol.EmbeddedResourceBlock> are needed.

#### Simple prompts

A prompt without arguments:

```csharp
[McpServerPromptType]
public class MyPrompts
{
    [McpServerPrompt, Description("A simple greeting prompt")]
    public static ChatMessage Greeting()
        => new(ChatRole.User, "Hello! How can you help me today?");
}
```

#### Prompts with arguments

Prompts can accept parameters to customize the generated messages. Use `[Description]` attributes to document each parameter. In addition to prompt arguments, methods can accept special parameter types that are resolved automatically: <xref:ModelContextProtocol.Server.McpServer>, `IProgress<ProgressNotificationValue>`, `ClaimsPrincipal`, and any service registered through dependency injection.

```csharp
[McpServerPromptType]
public class CodePrompts
{
    [McpServerPrompt, Description("Generates a code review prompt")]
    public static IEnumerable<ChatMessage> CodeReview(
        [Description("The programming language")] string language,
        [Description("The code to review")] string code) =>
        [
            new(ChatRole.User, $"Please review the following {language} code:\n\n```{language}\n{code}\n```"),
            new(ChatRole.Assistant, "I'll review the code for correctness, style, and potential improvements.")
        ];
    }
}
```

Register prompt types when building the server:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithPrompts<MyPrompts>()
    .WithPrompts<CodePrompts>();
```

### Rich content in prompts

Prompt messages can contain more than just text. For text and image content, use `ChatMessage` from Microsoft.Extensions.AI. `DataContent` is automatically mapped to the appropriate MCP content block: image MIME types become <xref:ModelContextProtocol.Protocol.ImageContentBlock>, audio MIME types become <xref:ModelContextProtocol.Protocol.AudioContentBlock>, and all other MIME types become <xref:ModelContextProtocol.Protocol.EmbeddedResourceBlock> with binary resource contents. For text embedded resources specifically, use <xref:ModelContextProtocol.Protocol.PromptMessage> directly.

#### Image content

Include images in prompts using `DataContent`:

```csharp
[McpServerPrompt, Description("A prompt that includes an image for analysis")]
public static IEnumerable<ChatMessage> AnalyzeImage(
    [Description("Instructions for the analysis")] string instructions)
{
    byte[] imageBytes = LoadSampleImage();
    return
    [
        new ChatMessage(ChatRole.User,
        [
            new TextContent($"Please analyze this image: {instructions}"),
            new DataContent(imageBytes, "image/png")
        ])
    ];
}
```

#### Embedded resources

For protocol-specific content types like <xref:ModelContextProtocol.Protocol.EmbeddedResourceBlock>, use <xref:ModelContextProtocol.Protocol.PromptMessage> instead of `ChatMessage`. `PromptMessage` has a `Role` property and a single `Content` property of type <xref:ModelContextProtocol.Protocol.ContentBlock>:

```csharp
[McpServerPrompt, Description("A prompt that includes a document resource")]
public static IEnumerable<PromptMessage> ReviewDocument(
    [Description("The document ID to review")] string documentId)
{
    string content = LoadDocument(documentId); // application logic to load by ID
    return
    [
        new PromptMessage
        {
            Role = Role.User,
            Content = new TextContentBlock { Text = "Please review the following document:" }
        },
        new PromptMessage
        {
            Role = Role.User,
            Content = new EmbeddedResourceBlock
            {
                Resource = new TextResourceContents
                {
                    Uri = $"docs://documents/{documentId}",
                    MimeType = "text/plain",
                    Text = content
                }
            }
        }
    ];
}
```

For binary resources, use the <xref:ModelContextProtocol.Protocol.BlobResourceContents.FromBytes*> factory method:

```csharp
new PromptMessage
{
    Role = Role.User,
    Content = new EmbeddedResourceBlock
    {
        Resource = BlobResourceContents.FromBytes(pdfBytes, "data://report.pdf", "application/pdf")
    }
}
```

### Consuming prompts on the client

Clients can discover and use prompts through <xref:ModelContextProtocol.Client.McpClient>.

#### Listing prompts

```csharp
IList<McpClientPrompt> prompts = await client.ListPromptsAsync();

foreach (var prompt in prompts)
{
    Console.WriteLine($"{prompt.Name}: {prompt.Description}");

    // Show available arguments
    if (prompt.ProtocolPrompt.Arguments is { Count: > 0 })
    {
        foreach (var arg in prompt.ProtocolPrompt.Arguments)
        {
            var required = arg.Required == true ? " (required)" : "";
            Console.WriteLine($"  - {arg.Name}: {arg.Description}{required}");
        }
    }
}
```

#### Getting a prompt

```csharp
GetPromptResult result = await client.GetPromptAsync(
    "code_review",
    new Dictionary<string, object?>
    {
        ["language"] = "csharp",
        ["code"] = "public static int Add(int a, int b) => a + b;"
    });

// Process the returned messages (PromptMessage has a single Content block)
foreach (var message in result.Messages)
{
    Console.WriteLine($"[{message.Role}]:");
    switch (message.Content)
    {
        case TextContentBlock text:
            Console.WriteLine($"  {text.Text}");
            break;
        case ImageContentBlock image:
            Console.WriteLine($"  [image] {image.MimeType}");
            break;
        case EmbeddedResourceBlock resource:
            Console.WriteLine($"  Resource: {resource.Resource.Uri}");
            break;
    }
}
```

### Prompt list change notifications

Servers can dynamically add, remove, or modify prompts at runtime and notify connected clients.

#### Sending notifications from the server

```csharp
// After adding or removing prompts dynamically
await server.SendNotificationAsync(
    NotificationMethods.PromptListChangedNotification,
    new PromptListChangedNotificationParams());
```

#### Handling notifications on the client

```csharp
mcpClient.RegisterNotificationHandler(
    NotificationMethods.PromptListChangedNotification,
    async (notification, cancellationToken) =>
    {
        var updatedPrompts = await mcpClient.ListPromptsAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"Prompt list updated. {updatedPrompts.Count} prompts available.");
    });
```
