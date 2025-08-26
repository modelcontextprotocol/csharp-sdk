---
title: Filters
author: halter73
description: MCP Server Handler Filters
uid: filters
---

# MCP Server Handler Filters

For each handler type in the MCP Server, there are corresponding `AddXXXFilter` methods in `McpServerBuilderExtensions.cs` that allow you to add filters to the handler pipeline. The filters are stored in `McpServerOptions.Filters` and applied during server configuration.

## Available Filter Methods

The following filter methods are available:

- `AddListResourceTemplatesFilter` - Filter for list resource templates handlers
- `AddListToolsFilter` - Filter for list tools handlers
- `AddCallToolFilter` - Filter for call tool handlers
- `AddListPromptsFilter` - Filter for list prompts handlers
- `AddGetPromptFilter` - Filter for get prompt handlers
- `AddListResourcesFilter` - Filter for list resources handlers
- `AddReadResourceFilter` - Filter for read resource handlers
- `AddCompleteFilter` - Filter for completion handlers
- `AddSubscribeToResourcesFilter` - Filter for resource subscription handlers
- `AddUnsubscribeFromResourcesFilter` - Filter for resource unsubscription handlers
- `AddSetLoggingLevelFilter` - Filter for logging level handlers

## Usage

Filters are functions that take a handler and return a new handler, allowing you to wrap the original handler with additional functionality:

```csharp
services.AddMcpServer()
    .WithListToolsHandler(async (context, cancellationToken) =>
    {
        // Your base handler logic
        return new ListToolsResult { Tools = GetTools() };
    })
    .AddListToolsFilter(next => async (context, cancellationToken) =>
    {
        // Pre-processing logic
        Console.WriteLine("Before handler execution");

        var result = await next(context, cancellationToken);

        // Post-processing logic
        Console.WriteLine("After handler execution");
        return result;
    });
```

## Filter Execution Order

```csharp
services.AddMcpServer()
    .WithListToolsHandler(baseHandler)
    .AddListToolsFilter(filter1)  // Executes first (outermost)
    .AddListToolsFilter(filter2)  // Executes second
    .AddListToolsFilter(filter3); // Executes third (closest to handler)
```

Execution flow: `filter1 -> filter2 -> filter3 -> baseHandler -> filter3 -> filter2 -> filter1`

## Common Use Cases

### Logging
```csharp
.AddListToolsFilter(next => async (context, cancellationToken) =>
{
    Console.WriteLine($"Processing request from {context.Meta.ProgressToken}");
    var result = await next(context, cancellationToken);
    Console.WriteLine($"Returning {result.Tools?.Count ?? 0} tools");
    return result;
});
```

### Error Handling
```csharp
.AddCallToolFilter(next => async (context, cancellationToken) =>
{
    try
    {
        return await next(context, cancellationToken);
    }
    catch (Exception ex)
    {
        return new CallToolResult
        {
            Content = new[] { new TextContent { Type = "text", Text = $"Error: {ex.Message}" } },
            IsError = true
        };
    }
});
```

### Performance Monitoring
```csharp
.AddListToolsFilter(next => async (context, cancellationToken) =>
{
    var stopwatch = Stopwatch.StartNew();
    var result = await next(context, cancellationToken);
    stopwatch.Stop();
    Console.WriteLine($"Handler took {stopwatch.ElapsedMilliseconds}ms");
    return result;
});
```

### Caching
```csharp
.AddListResourcesFilter(next => async (context, cancellationToken) =>
{
    var cacheKey = $"resources:{context.Params.Cursor}";
    if (cache.TryGetValue(cacheKey, out var cached))
        return cached;

    var result = await next(context, cancellationToken);
    cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
    return result;
});
```

## Built-in Authorization Filters

When using the ASP.NET Core integration (`ModelContextProtocol.AspNetCore`), authorization filters are automatically configured to support `[Authorize]` and `[AllowAnonymous]` attributes on MCP server tools, prompts, and resources.

### Authorization Attributes Support

The MCP server automatically respects the following authorization attributes:

- **`[Authorize]`** - Requires authentication for access
- **`[Authorize(Roles = "RoleName")]`** - Requires specific roles
- **`[Authorize(Policy = "PolicyName")]`** - Requires specific authorization policies
- **`[AllowAnonymous]`** - Explicitly allows anonymous access (overrides `[Authorize]`)

### Tool Authorization

Tools can be decorated with authorization attributes to control access:

```csharp
[McpServerToolType]
public class WeatherTools
{
    [McpServerTool, Description("Gets public weather data")]
    public static string GetWeather(string location)
    {
        return $"Weather for {location}: Sunny, 25°C";
    }

    [McpServerTool, Description("Gets detailed weather forecast")]
    [Authorize] // Requires authentication
    public static string GetDetailedForecast(string location)
    {
        return $"Detailed forecast for {location}: ...";
    }

    [McpServerTool, Description("Manages weather alerts")]
    [Authorize(Roles = "Admin")] // Requires Admin role
    public static string ManageWeatherAlerts(string alertType)
    {
        return $"Managing alert: {alertType}";
    }
}
```

### Class-Level Authorization

You can apply authorization at the class level, which affects all tools in the class:

```csharp
[McpServerToolType]
[Authorize] // All tools require authentication
public class RestrictedTools
{
    [McpServerTool, Description("Restricted tool accessible to authenticated users")]
    public static string RestrictedOperation()
    {
        return "Restricted operation completed";
    }

    [McpServerTool, Description("Public tool accessible to anonymous users")]
    [AllowAnonymous] // Overrides class-level [Authorize]
    public static string PublicOperation()
    {
        return "Public operation completed";
    }
}
```

### How Authorization Filters Work

The authorization filters work differently for list operations versus individual operations:

#### List Operations (ListTools, ListPrompts, ListResources)
For list operations, the filters automatically remove unauthorized items from the results. Users only see tools, prompts, or resources they have permission to access.

#### Individual Operations (CallTool, GetPrompt, ReadResource)
For individual operations, the filters return authorization errors when access is denied:

- **Tools**: Returns a `CallToolResult` with `IsError = true` and an error message
- **Prompts**: Throws an `McpException` with "Access forbidden" message
- **Resources**: Throws an `McpException` with "Access forbidden" message

### Setup Requirements

To use authorization features, you must configure authentication and authorization in your ASP.NET Core application:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => { /* JWT configuration */ })
    .AddMcp(options => { /* Resource metadata configuration */ });
builder.Services.AddAuthorization();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<WeatherTools>()
    .AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        // Custom call tool logic
        return await next(context, cancellationToken);
    });

var app = builder.Build();

app.MapMcp();
app.Run();
```

### Custom Authorization Filters

You can also create custom authorization filters using the filter methods:

```csharp
.AddCallToolFilter(next => async (context, cancellationToken) =>
{
    // Custom authorization logic
    if (context.User?.Identity?.IsAuthenticated != true)
    {
        return new CallToolResult
        {
            Content = [new TextContent { Text = "Custom: Authentication required" }],
            IsError = true
        };
    }

    return await next(context, cancellationToken);
});
```

### RequestContext

Within filters, you have access to:

- `context.User` - The current user's `ClaimsPrincipal`
- `context.Services` - The request's service provider for resolving authorization services
- `context.MatchedPrimitive` - The matched tool/prompt/resource with its metadata including authorization attributes via `context.MatchedPrimitive.Metadata`
