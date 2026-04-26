---
title: Identity and Role Propagation
author: halter73
description: How to access caller identity and roles in MCP tool, prompt, and resource handlers.
uid: identity
---

# Identity and Role Propagation

When building production MCP servers, you often need to know _who_ is calling a tool so you can enforce permissions, filter data, or audit access. The MCP C# SDK provides built-in support for propagating the caller's identity from the transport layer into your tool, prompt, and resource handlers — no custom headers or workarounds required.

## How Identity Flows Through the SDK

When a client sends a request over an authenticated HTTP transport (Streamable HTTP or SSE), the ASP.NET Core authentication middleware populates `HttpContext.User` with a `ClaimsPrincipal`. The SDK's transport layer automatically copies this `ClaimsPrincipal` into `JsonRpcMessage.Context.User`, which then flows through message filters, request filters, and finally into the handler or tool method.

```
HTTP Request (with auth token)
    → ASP.NET Core Authentication Middleware (populates HttpContext.User)
    → MCP Transport (copies User into JsonRpcMessage.Context.User)
    → Message Filters (context.User available)
    → Request Filters (context.User available)
    → Tool / Prompt / Resource Handler (ClaimsPrincipal injected as parameter)
```

This means you can access the authenticated user's identity at every stage of request processing.

## Direct `ClaimsPrincipal` Parameter Injection (Recommended)

The simplest and recommended approach is to declare a `ClaimsPrincipal` parameter on your tool method. The SDK automatically injects the authenticated user without including it in the tool's input schema:

```csharp
[McpServerToolType]
public class UserAwareTools
{
    [McpServerTool, Description("Returns a personalized greeting.")]
    public string Greet(ClaimsPrincipal? user, string message)
    {
        var userName = user?.Identity?.Name ?? "anonymous";
        return $"{userName}: {message}";
    }
}
```

This pattern works the same way for prompts and resources:

```csharp
[McpServerPromptType]
public class UserAwarePrompts
{
    [McpServerPrompt, Description("Creates a user-specific prompt.")]
    public ChatMessage PersonalizedPrompt(ClaimsPrincipal? user, string topic)
    {
        var userName = user?.Identity?.Name ?? "user";
        return new(ChatRole.User, $"As {userName}, explain {topic}.");
    }
}
```

### Why This Works

The SDK registers `ClaimsPrincipal` as one of the built-in services available during request processing. When a tool, prompt, or resource method declares a `ClaimsPrincipal` parameter, the SDK:

1. Excludes it from the generated JSON schema (clients never see it).
2. Automatically resolves it from the current request's `User` property at invocation time.
3. Passes `null` if no authenticated user is present (when the parameter is nullable).

This behavior is transport-agnostic. For HTTP transports, the `ClaimsPrincipal` comes from ASP.NET Core authentication. For other transports (like stdio), it will be `null` unless you set it explicitly via a message filter.

## Accessing Identity in Filters

Both message filters and request-specific filters expose the user via `context.User`:

```csharp
services.AddMcpServer()
    .WithRequestFilters(requestFilters =>
    {
        requestFilters.AddCallToolFilter(next => async (context, cancellationToken) =>
        {
            // Access user identity in a filter
            var userName = context.User?.Identity?.Name;
            var logger = context.Services?.GetService<ILogger<Program>>();
            logger?.LogInformation("Tool called by: {User}", userName ?? "anonymous");

            return await next(context, cancellationToken);
        });
    })
    .WithTools<UserAwareTools>();
```

## Role-Based Access with `[Authorize]` Attributes

For declarative authorization, you can use standard ASP.NET Core `[Authorize]` attributes on your tools, prompts, and resources. This requires calling `AddAuthorizationFilters()` during server configuration:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .AddAuthorizationFilters()
    .WithTools<RoleProtectedTools>();
```

Then decorate your tools with role requirements:

```csharp
[McpServerToolType]
public class RoleProtectedTools
{
    [McpServerTool, Description("Available to all authenticated users.")]
    [Authorize]
    public string GetData(string query)
    {
        return $"Data for: {query}";
    }

    [McpServerTool, Description("Admin-only operation.")]
    [Authorize(Roles = "Admin")]
    public string AdminOperation(string action)
    {
        return $"Admin action: {action}";
    }

    [McpServerTool, Description("Public tool accessible without authentication.")]
    [AllowAnonymous]
    public string PublicInfo()
    {
        return "This is public information.";
    }
}
```

When authorization fails, the SDK automatically:

- **For list operations**: Removes unauthorized items from the results so users only see what they can access.
- **For individual operations**: Returns a JSON-RPC error indicating access is forbidden.

See [Filters](xref:filters) for more details on authorization filters and their execution order.

## Using `IHttpContextAccessor` (HTTP-Only Alternative)

If you need access to the full `HttpContext` (not just the user), you can inject `IHttpContextAccessor` into your tool class. This gives you access to HTTP headers, query strings, and other request metadata:

```csharp
[McpServerToolType]
public class HttpContextTools(IHttpContextAccessor contextAccessor)
{
    [McpServerTool, Description("Returns data filtered by caller identity.")]
    public string GetFilteredData(string query)
    {
        var httpContext = contextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HTTP context available.");
        var userName = httpContext.User.Identity?.Name ?? "anonymous";
        return $"{userName}: results for '{query}'";
    }
}
```

> [!IMPORTANT]
> `IHttpContextAccessor` only works with HTTP transports. For transport-agnostic identity access, use `ClaimsPrincipal` parameter injection instead.

See [HTTP Context](xref:httpcontext) for more details, including important caveats about stale `HttpContext` with the legacy SSE transport.

## Transport Considerations

| Transport | Identity Source | Notes |
| --- | --- | --- |
| Streamable HTTP | ASP.NET Core authentication middleware populates `HttpContext.User`, which the transport copies to each request. | Recommended for production. Each request carries fresh authentication context. |
| SSE | Same as Streamable HTTP, but the `HttpContext` is tied to the long-lived SSE connection. | The `ClaimsPrincipal` parameter injection still works correctly, but `IHttpContextAccessor` may return stale claims if the client's token was refreshed after the SSE connection was established. |
| Stdio | No built-in authentication. `ClaimsPrincipal` is `null` unless set via a message filter. | For process-level identity, you can set the user in a message filter based on environment variables or other process-level context. |

### Setting Identity for Stdio Transport

For stdio-based servers where the caller's identity comes from the process environment rather than HTTP authentication, you can set the user in a message filter:

```csharp
services.AddMcpServer()
    .WithMessageFilters(messageFilters =>
    {
        messageFilters.AddIncomingFilter(next => async (context, cancellationToken) =>
        {
            // Set user based on process-level context
            var role = Environment.GetEnvironmentVariable("MCP_USER_ROLE") ?? "default";
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "stdio-user"), new Claim(ClaimTypes.Role, role)],
                "StdioAuth", ClaimTypes.Name, ClaimTypes.Role));

            await next(context, cancellationToken);
        });
    })
    .WithTools<UserAwareTools>();
```

## Full Example: Protected HTTP Server

For a complete example of an MCP server with JWT authentication, OAuth resource metadata, and protected tools, see the [ProtectedMcpServer sample](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpServer).

The sample demonstrates:

- Configuring JWT Bearer authentication
- Setting up MCP authentication with resource metadata
- Using `RequireAuthorization()` to protect the MCP endpoint
- Implementing weather tools that require authentication
