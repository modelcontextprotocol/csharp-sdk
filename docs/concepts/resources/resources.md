---
title: Resources
author: jeffhandley
description: How to implement and consume MCP resources for exposing data to clients.
uid: resources
---

## Resources

MCP [resources] allow servers to expose data and content to clients. Resources represent any kind of data that a server wants to make available&mdash;files, database records, API responses, live system data, and more.

[resources]: https://modelcontextprotocol.io/specification/2025-11-25/server/resources

This document covers implementing resources on the server, consuming them from the client, resource templates, subscriptions, and change notifications.

### Defining resources on the server

Resources can be defined in several ways:

- Using the <xref:ModelContextProtocol.Server.McpServerResourceAttribute> attribute on methods within a class marked with <xref:ModelContextProtocol.Server.McpServerResourceTypeAttribute>
- Using <xref:ModelContextProtocol.Server.McpServerResource.Create*> factory methods from a delegate, `MethodInfo`, or `AIFunction`
- Deriving from <xref:ModelContextProtocol.Server.McpServerResource> or <xref:ModelContextProtocol.Server.DelegatingMcpServerResource>
- Implementing a custom <xref:ModelContextProtocol.Server.McpRequestHandler`2> via <xref:ModelContextProtocol.Server.McpServerHandlers>
- Implementing a low-level <xref:ModelContextProtocol.Server.McpRequestFilter`2>

The attribute-based approach is the most common and is shown throughout this document.

#### Direct resources

Direct resources have a fixed URI and are returned in the resource list:

```csharp
[McpServerResourceType]
public class MyResources
{
    [McpServerResource(UriTemplate = "config://app/settings", Name = "App Settings", MimeType = "application/json")]
    [Description("Returns application configuration settings")]
    public static string GetSettings() => JsonSerializer.Serialize(new { theme = "dark", language = "en" });
}
```

#### Template resources

Template resources use [URI templates (RFC 6570)] with parameters. They are returned separately in the resource templates list and can match a range of URIs:

[URI templates (RFC 6570)]: https://tools.ietf.org/html/rfc6570

```csharp
[McpServerResourceType]
public class DocumentResources
{
    [McpServerResource(UriTemplate = "docs://articles/{id}", Name = "Article")]
    [Description("Returns an article by its ID")]
    public static ResourceContents GetArticle(string id)
    {
        string? content = LoadArticle(id); // application logic to load by ID

        if (content is null)
        {
            throw new McpException($"Article not found: {id}");
        }

        return new TextResourceContents
        {
            Uri = $"docs://articles/{id}",
            MimeType = "text/plain",
            Text = content
        };
    }
}
```

Register resource types when building the server:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithResources<MyResources>()
    .WithResources<DocumentResources>();
```

### Reading text resources

Text resources return their content as <xref:ModelContextProtocol.Protocol.TextResourceContents> with a `Text` property:

```csharp
[McpServerResource(UriTemplate = "notes://daily/{date}", Name = "Daily Notes")]
[Description("Returns notes for a given date")]
public static TextResourceContents GetDailyNotes(string date)
{
    return new TextResourceContents
    {
        Uri = $"notes://daily/{date}",
        MimeType = "text/markdown",
        Text = $"# Notes for {date}\n\n- Meeting at 10am\n- Review PRs"
    };
}
```

### Reading binary resources

Binary resources return their content as <xref:ModelContextProtocol.Protocol.BlobResourceContents> with a `Blob` property containing the raw bytes. Use the <xref:ModelContextProtocol.Protocol.BlobResourceContents.FromBytes*> factory method to construct instances:

```csharp
[McpServerResource(UriTemplate = "images://photos/{id}", Name = "Photo")]
[Description("Returns a photo by ID")]
public static BlobResourceContents GetPhoto(int id)
{
    byte[] imageData = LoadPhoto(id);
    return BlobResourceContents.FromBytes(imageData, $"images://photos/{id}", "image/png");
}
```

### Consuming resources on the client

Clients can discover and read resources using <xref:ModelContextProtocol.Client.McpClient>:

#### Listing resources

```csharp
// List direct resources
IList<McpClientResource> resources = await client.ListResourcesAsync();

foreach (var resource in resources)
{
    Console.WriteLine($"{resource.Name} ({resource.Uri})");
    Console.WriteLine($"  MIME: {resource.MimeType}");
    Console.WriteLine($"  Description: {resource.Description}");
}
```

#### Listing resource templates

```csharp
// List resource templates (parameterized URIs)
IList<McpClientResourceTemplate> templates = await client.ListResourceTemplatesAsync();

foreach (var template in templates)
{
    Console.WriteLine($"{template.Name}: {template.UriTemplate}");
}
```

#### Reading a resource

```csharp
// Read a direct resource by URI
ReadResourceResult result = await client.ReadResourceAsync("config://app/settings");

foreach (var content in result.Contents)
{
    if (content is TextResourceContents text)
    {
        Console.WriteLine($"[{text.MimeType}] {text.Text}");
    }
    else if (content is BlobResourceContents blob)
    {
        Console.WriteLine($"[{blob.MimeType}] {blob.Blob.Length} bytes");
    }
}
```

#### Reading a template resource

```csharp
// Read a resource using a URI template with parameter values
ReadResourceResult result = await client.ReadResourceAsync(
    "file:///{path}",
    new Dictionary<string, object?> { ["path"] = "docs/readme.md" });
```

### Resource subscriptions

Clients can subscribe to resource updates to be notified when a resource's content changes. The server must declare subscription support in its capabilities.

#### Subscribing on the client

```csharp
// Subscribe with an inline handler
IAsyncDisposable subscription = await client.SubscribeToResourceAsync(
    "config://app/settings",
    async (notification, cancellationToken) =>
    {
        Console.WriteLine($"Resource updated: {notification.Uri}");

        // Re-read the resource to get updated content
        var updated = await client.ReadResourceAsync(notification.Uri, cancellationToken: cancellationToken);
        // Process updated content...
    });

// Later, unsubscribe by disposing
await subscription.DisposeAsync();
```

Clients can also subscribe and unsubscribe separately:

```csharp
// Subscribe without a handler (use a global notification handler instead)
await client.SubscribeToResourceAsync("config://app/settings");

// Unsubscribe when no longer interested
await client.UnsubscribeFromResourceAsync("config://app/settings");
```

#### Handling subscriptions on the server

Register subscription handlers when building the server:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithResources<MyResources>()
    .WithSubscribeToResourcesHandler(async (ctx, ct) =>
    {
        if (ctx.Params?.Uri is { } uri)
        {
            // Track the subscription (e.g., in a concurrent dictionary)
            subscriptions[ctx.Server.SessionId].TryAdd(uri, 0);
        }
        return new EmptyResult();
    })
    .WithUnsubscribeFromResourcesHandler(async (ctx, ct) =>
    {
        if (ctx.Params?.Uri is { } uri)
        {
            subscriptions[ctx.Server.SessionId].TryRemove(uri, out _);
        }
        return new EmptyResult();
    });
```

#### Sending resource update notifications

When a resource's content changes, the server notifies subscribed clients:

```csharp
// Notify that a specific resource was updated
await server.SendNotificationAsync(
    NotificationMethods.ResourceUpdatedNotification,
    new ResourceUpdatedNotificationParams { Uri = "config://app/settings" });
```

### Resource list change notifications

When the set of available resources changes (resources added or removed), the server notifies clients:

#### Sending notifications from the server

```csharp
// After adding or removing resources dynamically
await server.SendNotificationAsync(
    NotificationMethods.ResourceListChangedNotification,
    new ResourceListChangedNotificationParams());
```

#### Handling notifications on the client

```csharp
mcpClient.RegisterNotificationHandler(
    NotificationMethods.ResourceListChangedNotification,
    async (notification, cancellationToken) =>
    {
        // Refresh the resource list
        var updatedResources = await mcpClient.ListResourcesAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"Resource list updated. {updatedResources.Count} resources available.");
    });
```
