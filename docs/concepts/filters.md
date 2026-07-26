---
title: Filters
author: halter73
description: MCP Server Filters
uid: filters
---

# MCP Server Filters

The MCP Server provides two levels of filters for intercepting and modifying request processing:

1. **Message Filters** - Low-level filters (`AddIncomingFilter`, `AddOutgoingFilter`) configured via `WithMessageFilters(...)` that intercept all JSON-RPC messages before routing.
2. **Request-Specific Filters** - Handler-level filters (e.g., `AddListToolsFilter`, `AddCallToolFilter`) configured via `WithRequestFilters(...)` that target specific MCP operations.

The filters are stored in `McpServerOptions.Filters`.

## Available Request-Specific Filter Methods

The following request filter methods are available on `IMcpRequestFilterBuilder` inside `WithRequestFilters(...)`:

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

## Message Filters

In addition to the request-specific filters above, there are low-level message filters that intercept all JSON-RPC messages before they are routed to specific handlers.
Configure these on `IMcpMessageFilterBuilder` inside `WithMessageFilters(...)`:

- `AddIncomingFilter` - Filter for all incoming JSON-RPC messages (requests and notifications)
- `AddOutgoingFilter` - Filter for all outgoing JSON-RPC messages (responses and notifications)

### When to Use Message Filters

Message filters operate at a lower level than request-specific filters and are useful when you need to:

- Intercept all messages regardless of type
- Implement custom protocol extensions or handle custom JSON-RPC methods
- Log or monitor all traffic between client and server
- Modify or skip messages before they reach handlers
- Send additional messages in response to specific events

### Incoming Message Filter

`AddIncomingFilter` intercepts all incoming JSON-RPC messages before they are dispatched to request-specific handlers:

```csharp
services.AddMcpServer()
    .WithMessageFilters(messageFilters =>
    {
        messageFilters.AddIncomingFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?.GetService<ILogger<Program>>();

            // Access the raw JSON-RPC message
            if (context.JsonRpcMessage is JsonRpcRequest request)
            {
                logger?.LogInformation($"Incoming request: {request.Method}");
            }

            // Call next to continue processing
            await next(context, cancellationToken);
        });
    })
    .WithTools<MyTools>();
```

#### MessageContext Properties

Inside an incoming message filter, you have access to:

- `context.JsonRpcMessage` - The incoming `JsonRpcMessage` (can be `JsonRpcRequest` or `JsonRpcNotification`)
- `context.Server` - The `McpServer` instance for sending responses or notifications
- `context.Services` - The request's service provider
- `context.Items` - A dictionary for passing data between filters

#### Skipping Default Handlers

You can skip the default handler by not calling `next`. This is useful for implementing custom protocol methods:

```csharp
.WithMessageFilters(messageFilters =>
{
    messageFilters.AddIncomingFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "custom/myMethod")
        {
            // Handle the custom method directly
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = JsonSerializer.SerializeToNode(new { message = "Custom response" })
            };
            await context.Server.SendMessageAsync(response, cancellationToken);
            return; // Don't call next - we handled it
        }

        await next(context, cancellationToken);
    });
})
```

### Outgoing Message Filter

`AddOutgoingFilter` intercepts all outgoing JSON-RPC messages before they are sent to the client:

```csharp
services.AddMcpServer()
    .WithMessageFilters(messageFilters =>
    {
        messageFilters.AddOutgoingFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?.GetService<ILogger<Program>>();

            // Inspect outgoing messages
            switch (context.JsonRpcMessage)
            {
                case JsonRpcResponse response:
                    logger?.LogInformation($"Sending response for request {response.Id}");
                    break;
                case JsonRpcNotification notification:
                    logger?.LogInformation($"Sending notification: {notification.Method}");
                    break;
            }

            await next(context, cancellationToken);
        });
    })
    .WithTools<MyTools>();
```

#### Skipping Outgoing Messages

You can suppress outgoing messages by not calling `next`:

```csharp
.WithMessageFilters(messageFilters =>
{
    messageFilters.AddOutgoingFilter(next => async (context, cancellationToken) =>
    {
        // Suppress specific notifications
        if (context.JsonRpcMessage is JsonRpcNotification notification &&
            notification.Method == "notifications/progress")
        {
            return; // Don't send this notification
        }

        await next(context, cancellationToken);
    });
})
```

#### Sending Additional Messages

Outgoing message filters can send additional messages by calling `next` with a new `MessageContext`:

```csharp
.WithMessageFilters(messageFilters =>
{
    messageFilters.AddOutgoingFilter(next => async (context, cancellationToken) =>
    {
        // Send an extra notification before certain responses
        if (context.JsonRpcMessage is JsonRpcResponse response &&
            response.Result is JsonObject result &&
            result.ContainsKey("tools"))
        {
            var notification = new JsonRpcNotification
            {
                Method = "custom/toolsListed",
                Params = new JsonObject { ["timestamp"] = DateTime.UtcNow.ToString("O") },
                Context = new JsonRpcMessageContext
                {
                    RelatedTransport = context.JsonRpcMessage.Context?.RelatedTransport
                }
            };
            await next(new MessageContext(context.Server, notification), cancellationToken);
        }

        await next(context, cancellationToken);
    });
})
```

### Message Filter Execution Order

Message filters execute in registration order, with the first registered filter being the outermost:

```csharp
services.AddMcpServer()
    .WithMessageFilters(messageFilters =>
    {
        messageFilters.AddIncomingFilter(incomingFilter1); // Incoming: executes first (outermost)
        messageFilters.AddIncomingFilter(incomingFilter2); // Incoming: executes second
        messageFilters.AddOutgoingFilter(outgoingFilter1); // Outgoing: executes first (outermost)
        messageFilters.AddOutgoingFilter(outgoingFilter2); // Outgoing: executes second
    })
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(toolsFilter);    // Request-specific filter
    })
    .WithTools<MyTools>();
```

**Important**: Incoming message filters always run before request-specific filters, and outgoing message filters run when responses or notifications are sent. The complete execution flow for a request/response cycle is:

```
Request arrives
    ↓
IncomingFilter1 (before next)
    ↓
IncomingFilter2 (before next)
    ↓
Request Routing → ListToolsFilter → Handler
    ↓
IncomingFilter2 (after next)
    ↓
IncomingFilter1 (after next)
    ↓
Response sent via OutgoingFilter1 (before next)
    ↓
OutgoingFilter2 (before next)
    ↓
Transport sends message
    ↓
OutgoingFilter2 (after next)
    ↓
OutgoingFilter1 (after next)
```

### Passing Data Between Filters

The `Items` dictionary allows you to pass data between filters processing the same message:

```csharp
.WithMessageFilters(messageFilters =>
{
    messageFilters.AddIncomingFilter(next => async (context, cancellationToken) =>
    {
        context.Items["requestStartTime"] = DateTime.UtcNow;
        await next(context, cancellationToken);
    });

    messageFilters.AddIncomingFilter(next => async (context, cancellationToken) =>
    {
        await next(context, cancellationToken);

        if (context.Items.TryGetValue("requestStartTime", out var startTime))
        {
            var elapsed = DateTime.UtcNow - (DateTime)startTime;
            var logger = context.Services?.GetService<ILogger<Program>>();
            logger?.LogInformation($"Request processed in {elapsed.TotalMilliseconds}ms");
        }
    });
})
```

## Usage

Filters are functions that take a handler and return a new handler, allowing you to wrap the original handler with additional functionality:

```csharp
services.AddMcpServer()
    .WithListToolsHandler(async (context, cancellationToken) =>
    {
        // Your base handler logic
        return new ListToolsResult { Tools = GetTools() };
    })
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?.GetService<ILogger<Program>>();

            // Pre-processing logic
            logger?.LogInformation("Before handler execution");

            var result = await next(context, cancellationToken);

            // Post-processing logic
            logger?.LogInformation("After handler execution");
            return result;
        });
    });
```

## Filter Execution Order

```csharp
services.AddMcpServer()
    .WithListToolsHandler(baseHandler)
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(filter1); // Executes first (outermost)
        requestFilters.AddListToolsFilter(filter2); // Executes second
        requestFilters.AddListToolsFilter(filter3); // Executes third (closest to handler)
    });
```

Execution flow: `filter1 -> filter2 -> filter3 -> baseHandler -> filter3 -> filter2 -> filter1`

## Common Use Cases

### Logging

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddListToolsFilter(next => async (context, cancellationToken) =>
    {
        var logger = context.Services?.GetService<ILogger<Program>>();

        logger?.LogInformation($"Processing request from {context.Params?.ProgressToken}");
        var result = await next(context, cancellationToken);
        logger?.LogInformation($"Returning {result.Tools?.Count ?? 0} tools");
        return result;
    });
});
```

### Error Handling

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        try
        {
            return await next(context, cancellationToken);
        }
        catch (Exception ex)
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Error while processing CallTool request for {ProgressToken}", context.Params?.ProgressToken);

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "An unexpected error occurred while processing the tool call." }],
                IsError = true
            };
        }
    });
});
```

### Performance Monitoring

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddListToolsFilter(next => async (context, cancellationToken) =>
    {
        var logger = context.Services?.GetService<ILogger<Program>>();

        var stopwatch = Stopwatch.StartNew();
        var result = await next(context, cancellationToken);
        stopwatch.Stop();
        logger?.LogInformation($"Handler took {stopwatch.ElapsedMilliseconds}ms");
        return result;
    });
});
```

### Caching

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddListResourcesFilter(next => async (context, cancellationToken) =>
    {
        var cache = context.Services!.GetRequiredService<IMemoryCache>();

        var cacheKey = $"resources:{context.Params.Cursor}";
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return (ListResourcesResult)cached;
        }

        var result = await next(context, cancellationToken);
        cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    });
});
```

## Built-in Authorization Request Filters

When using the ASP.NET Core integration (`ModelContextProtocol.AspNetCore`), you can add authorization filters to support `[Authorize]` and `[AllowAnonymous]` attributes on MCP server tools, prompts, and resources by calling `AddAuthorizationFilters()` on your MCP server builder.

### Enabling Authorization Request Filters

To enable authorization support, call `AddAuthorizationFilters()` when configuring your MCP server:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters() // Enable authorization filter support
    .WithTools<WeatherTools>();
```

**Important**: You should always call `AddAuthorizationFilters()` when using ASP.NET Core integration if you want to use authorization attributes like `[Authorize]` on your MCP server tools, prompts, or resources.

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

For individual operations, the filters throw an `McpException` with "Access forbidden" message. These get turned into JSON-RPC errors if uncaught by middleware.

### Filter Execution Order and Authorization

Authorization filters are applied automatically when you call `AddAuthorizationFilters()`. These filters run at a specific point in the filter pipeline, which means:

**Filters added before authorization filters** can see:

- Unauthorized requests for operations before they are rejected by the authorization filters.
- Complete listings for unauthorized primitives before they are filtered out by the authorization filters.

**Filters added after authorization filters** will only see:

- Authorized requests that passed authorization checks.
- Filtered listings containing only authorized primitives.

This allows you to implement logging, metrics, or other cross-cutting concerns that need to see all requests, while still maintaining proper authorization:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?.GetService<ILogger<Program>>();

            // This filter runs BEFORE authorization - sees all tools
            logger?.LogInformation("Request for tools list - will see all tools");
            var result = await next(context, cancellationToken);
            logger?.LogInformation($"Returning {result.Tools?.Count ?? 0} tools after authorization");
            return result;
        });
    })
    .AddAuthorizationFilters() // Authorization filtering happens here
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            var logger = context.Services?.GetService<ILogger<Program>>();

            // This filter runs AFTER authorization - only sees authorized tools
            var result = await next(context, cancellationToken);
            logger?.LogInformation($"Post-auth filter sees {result.Tools?.Count ?? 0} authorized tools");
            return result;
        });
    })
    .WithTools<WeatherTools>();
```

### Setup Requirements

To use authorization features, you must configure authentication and authorization in your ASP.NET Core application and call `AddAuthorizationFilters()`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => { /* JWT configuration */ })
    .AddMcp(options => { /* Resource metadata configuration */ });
builder.Services.AddAuthorization();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters() // Required for authorization support
    .WithTools<WeatherTools>()
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            // Custom call tool logic
            return await next(context, cancellationToken);
        });
    });

var app = builder.Build();

app.MapMcp();
app.Run();
```

### Custom Authorization Filters

You can also create custom authorization filters using the filter methods:

```csharp
.WithRequestFilters(requestFilters =>
{
    requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        // Custom authorization logic
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "Custom: Authentication required" }],
                IsError = true
            };
        }

        return await next(context, cancellationToken);
    });
});
```

### RequestContext

Within filters, you have access to:

- `context.User` - The current user's `ClaimsPrincipal`.
- `context.Services` - The request's service provider for resolving authorization services.
- `context.MatchedPrimitive` - The matched tool/prompt/resource with its metadata including authorization attributes via `context.MatchedPrimitive.Metadata`.
