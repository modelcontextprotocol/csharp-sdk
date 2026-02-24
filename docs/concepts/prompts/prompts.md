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

Prompts are defined as methods marked with the <xref:ModelContextProtocol.Server.McpServerPromptAttribute> attribute within a class marked with <xref:ModelContextProtocol.Server.McpServerPromptTypeAttribute>. Prompts can return `ChatMessage` instances for simple text/image content, or <xref:ModelContextProtocol.Protocol.PromptMessage> instances when protocol-specific content types like <xref:ModelContextProtocol.Protocol.EmbeddedResourceBlock> are needed.

#### Simple prompts

A prompt without arguments returns a fixed message:

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

Prompts can accept parameters to customize the generated messages. Use `[Description]` attributes to document each parameter:

```csharp
[McpServerPromptType]
public class CodePrompts
{
    [McpServerPrompt, Description("Generates a code review prompt")]
    public static IEnumerable<ChatMessage> CodeReview(
        [Description("The programming language")] string language,
        [Description("The code to review")] string code)
    {
        return
        [
            new ChatMessage(ChatRole.User,
                $"Please review the following {language} code:\n\n```{language}\n{code}\n```"),
            new ChatMessage(ChatRole.Assistant,
                "I'll review the code for correctness, style, and potential improvements.")
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

Prompt messages can contain more than just text. For text and image content, use `ChatMessage` from Microsoft.Extensions.AI. For protocol-specific content types like embedded resources, use <xref:ModelContextProtocol.Protocol.PromptMessage> instead.

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
[McpServerPrompt, Description("A prompt that includes a file resource")]
public static IEnumerable<PromptMessage> ReviewDocument(
    [Description("Path to the document to review")] string path)
{
    string content = File.ReadAllText(path);
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
                    Uri = $"file:///{path}",
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
