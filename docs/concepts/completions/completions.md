---
title: Completions
author: jeffhandley
description: How to implement and use argument auto-completion for prompts and resources.
uid: completions
---

## Completions

MCP [completions] allow servers to provide argument auto-completion suggestions for prompt and resource template parameters. This helps clients offer a better user experience by suggesting valid values as the user types.

[completions]: https://modelcontextprotocol.io/specification/2025-11-25/server/utilities/completion

### Overview

Completions work with two types of references:

- **Prompt argument completions**: Suggest values for prompt parameters (e.g., language names, style options)
- **Resource template argument completions**: Suggest values for URI template parameters (e.g., file paths, resource IDs)

The server returns a <xref:ModelContextProtocol.Protocol.Completion> object containing a list of suggested values, an optional total count, and a flag indicating if more values are available.

### Implementing completions on the server

Register a completion handler when building the server. The handler receives a reference (prompt or resource template) and the current argument value:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithPrompts<MyPrompts>()
    .WithResources<MyResources>()
    .WithCompleteHandler(async (ctx, ct) =>
    {
        if (ctx.Params is not { } @params)
            throw new McpProtocolException("Params are required.", McpErrorCode.InvalidParams);

        var argument = @params.Argument;

        // Handle prompt argument completions
        if (@params.Ref is PromptReference promptRef)
        {
            var suggestions = argument.Name switch
            {
                "language" => new[] { "csharp", "python", "javascript", "typescript", "go", "rust" },
                "style" => new[] { "casual", "formal", "technical", "friendly" },
                _ => Array.Empty<string>()
            };

            // Filter suggestions based on what the user has typed so far
            var filtered = suggestions.Where(s => s.StartsWith(argument.Value, StringComparison.OrdinalIgnoreCase)).ToList();

            return new CompleteResult
            {
                Completion = new Completion
                {
                    Values = filtered,
                    Total = filtered.Count,
                    HasMore = false
                }
            };
        }

        // Handle resource template argument completions
        if (@params.Ref is ResourceTemplateReference resourceRef)
        {
            var availableIds = new[] { "1", "2", "3", "4", "5" };
            var filtered = availableIds.Where(id => id.StartsWith(argument.Value)).ToList();

            return new CompleteResult
            {
                Completion = new Completion
                {
                    Values = filtered,
                    Total = filtered.Count,
                    HasMore = false
                }
            };
        }

        return new CompleteResult();
    });
```

### Requesting completions on the client

Clients request completions using <xref:ModelContextProtocol.Client.McpClient.CompleteAsync*>. Provide a reference to the prompt or resource template, the argument name, and the current partial value:

#### Prompt argument completions

```csharp
// Get completions for a prompt argument
CompleteResult result = await client.CompleteAsync(
    new PromptReference { Name = "code_review" },
    argumentName: "language",
    argumentValue: "type");

// result.Completion.Values might contain: ["typescript"]
foreach (var suggestion in result.Completion.Values)
{
    Console.WriteLine($"  {suggestion}");
}

if (result.Completion.HasMore == true)
{
    Console.WriteLine($"  ... and more ({result.Completion.Total} total)");
}
```

#### Resource template argument completions

```csharp
// Get completions for a resource template argument
CompleteResult result = await client.CompleteAsync(
    new ResourceTemplateReference { Uri = "file:///{path}" },
    argumentName: "path",
    argumentValue: "src/");

foreach (var suggestion in result.Completion.Values)
{
    Console.WriteLine($"  {suggestion}");
}
```
