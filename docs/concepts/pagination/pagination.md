---
title: Pagination
author: jeffhandley
description: How to use cursor-based pagination when listing tools, prompts, and resources.
uid: pagination
---

## Pagination

MCP uses [cursor-based pagination] for all list operations that may return large result sets.

[cursor-based pagination]: https://modelcontextprotocol.io/specification/2025-11-25/server/utilities/pagination

### Overview

Instead of offset-based pagination (page 1, page 2, etc.), MCP uses opaque cursor tokens. Each paginated response may include a `NextCursor` value. If present, pass it in the next request to retrieve the next page of results.

Two levels of API are provided for paginated operations:

1. **Convenience methods** (e.g., `ListToolsAsync()` returning `IList<T>`) that automatically handle pagination and return all results.
2. **Raw methods** (e.g., `ListToolsAsync(ListToolsRequestParams)` returning the result type directly) that provide direct control over pagination.

### Automatic pagination

The convenience methods on <xref:ModelContextProtocol.Client.McpClient> handle pagination automatically, fetching all pages and returning the complete list:

```csharp
// Fetches all tools, handling pagination automatically
IList<McpClientTool> allTools = await client.ListToolsAsync();

// Fetches all resources, handling pagination automatically
IList<McpClientResource> allResources = await client.ListResourcesAsync();

// Fetches all prompts, handling pagination automatically
IList<McpClientPrompt> allPrompts = await client.ListPromptsAsync();

// Fetches all resource templates, handling pagination automatically
IList<McpClientResourceTemplate> allTemplates = await client.ListResourceTemplatesAsync();
```

### Manual pagination

For more control, use the raw methods that accept request parameters and return paginated results. This is useful for processing results page by page or limiting the number of results retrieved:

```csharp
string? cursor = null;

do
{
    var result = await client.ListToolsAsync(new ListToolsRequestParams
    {
        Cursor = cursor
    });

    // Process this page of results
    foreach (var tool in result.Tools)
    {
        Console.WriteLine($"{tool.Name}: {tool.Description}");
    }

    // Get the cursor for the next page (null when no more pages)
    cursor = result.NextCursor;

} while (cursor is not null);
```

### Pagination on the server

When implementing custom list handlers on the server, pagination is supported by examining the `Cursor` property of the request parameters and returning a `NextCursor` in the result:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithListResourcesHandler(async (ctx, ct) =>
    {
        const int pageSize = 10;
        int startIndex = 0;

        // Parse cursor to determine starting position
        if (ctx.Params?.Cursor is { } cursor)
        {
            startIndex = int.Parse(cursor);
        }

        var allResources = GetAllResources();
        var page = allResources.Skip(startIndex).Take(pageSize).ToList();
        var hasMore = startIndex + pageSize < allResources.Count;

        return new ListResourcesResult
        {
            Resources = page,
            NextCursor = hasMore ? (startIndex + pageSize).ToString() : null
        };
    });
```

> [!NOTE]
> The cursor format is opaque to the client. Servers can use any encoding scheme (numeric offsets, encoded tokens, database cursors, etc.) as long as they can parse their own cursors on subsequent requests.

> [!NOTE]
> Because the cursor format is opaque to the client, _any_ value specified in the cursor, including the empty string, signals that more results are available. If an MCP server erroneously sends an empty string cursor with the final page of results, clients can implement their own low-level pagination scheme to work around this case.
