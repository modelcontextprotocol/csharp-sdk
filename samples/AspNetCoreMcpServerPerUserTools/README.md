# ASP.NET Core MCP Server with Per-User Tool Filtering

This sample demonstrates how to create an MCP (Model Context Protocol) server that provides different sets of tools to different users based on their authentication and permissions. This addresses the requirement from [issue #714](https://github.com/modelcontextprotocol/csharp-sdk/issues/714) to support varying the list of available tools/resources per user.

## Overview

The sample showcases the technique described by @halter73 in issue #714, using the `ConfigureSessionOptions` callback to dynamically modify the `ToolCollection` based on user permissions for each MCP session.

## Features

- **Per-User Tool Filtering**: Different users see different tools based on their role
- **Three Permission Levels**: 
  - **Public**: Basic tools available to all users (echo, time)
  - **User**: Additional tools for authenticated users (user info, calculator)
  - **Admin**: Full access including system administration tools
- **Header-based Authentication**: Simple authentication mechanism using HTTP headers
- **Dynamic Tool Loading**: Tools are filtered per session, not globally
- **Audit Logging**: Logs user sessions and tool access for monitoring

## Tool Categories

### Public Tools (`PublicTool.cs`)
Available to all users without authentication:
- `echo` - Echo messages back to the client
- `get_time` - Get current server time

### User Tools (`UserTool.cs`) 
Available to authenticated users:
- `get_user_info` - Get personalized user information
- `calculate` - Perform basic mathematical calculations

### Admin Tools (`AdminTool.cs`)
Available only to administrators:
- `get_system_status` - View system status and performance metrics
- `manage_config` - Manage server configuration settings
- `view_audit_logs` - View audit logs and user activity

## Authentication

The sample uses a simple header-based authentication system suitable for development and testing. In production, replace this with proper authentication/authorization (e.g., JWT, OAuth, ASP.NET Core Identity).

### Authentication Headers

#### Option 1: Role-based Headers
```bash
# Admin user
X-User-Id: admin-john
X-User-Role: admin

# Regular user  
X-User-Id: user-alice
X-User-Role: user

# Public user
X-User-Id: public-bob
X-User-Role: public
```

#### Option 2: Bearer Token Pattern
```bash
# Admin user
Authorization: Bearer admin-token123

# Regular user
Authorization: Bearer user-token456

# Public user  
Authorization: Bearer token789
```

## Running the Sample

1. **Build and run the server:**
   ```bash
   cd samples/AspNetCoreMcpServerPerUserTools
   dotnet run
   ```

2. **Test authentication endpoint:**
   ```bash
   # Test admin user
   curl -H "X-User-Role: admin" -H "X-User-Id: admin-john" \
        http://localhost:3001/test-auth

   # Test regular user
   curl -H "X-User-Role: user" -H "X-User-Id: user-alice" \
        http://localhost:3001/test-auth
   ```

3. **Connect MCP client and test tool filtering:**
   
   The MCP server will be available at `http://localhost:3001/` for MCP protocol connections.

## Testing Tool Access

### As Anonymous User (Public Tools Only)
```bash
# Will see only: echo, get_time
curl -X POST http://localhost:3001/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### As Regular User 
```bash  
# Will see: echo, get_time, get_user_info, calculate
curl -X POST http://localhost:3001/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "X-User-Role: user" \
  -H "X-User-Id: user-alice" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### As Admin User
```bash
# Will see all tools: echo, get_time, get_user_info, calculate, get_system_status, manage_config, view_audit_logs  
curl -X POST http://localhost:3001/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "X-User-Role: admin" \
  -H "X-User-Id: admin-john" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

## How It Works

### 1. Tool Registration
All tools are registered during startup using the normal MCP tool registration:

```csharp
builder.Services.AddMcpServer()
    .WithTools<PublicTool>()
    .WithTools<UserTool>()
    .WithTools<AdminTool>();
```

### 2. Per-Session Filtering
The key technique is using `ConfigureSessionOptions` to modify the tool collection per session:

```csharp
.WithHttpTransport(options =>
{
    options.ConfigureSessionOptions = async (httpContext, mcpOptions, cancellationToken) =>
    {
        var userRole = GetUserRole(httpContext);
        var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
        
        if (toolCollection != null)
        {
            // Clear all tools and add back only those allowed for this user
            toolCollection.Clear();
            
            switch (userRole)
            {
                case "admin":
                    AddToolsForType<PublicTool>(toolCollection);
                    AddToolsForType<UserTool>(toolCollection);  
                    AddToolsForType<AdminTool>(toolCollection);
                    break;
                case "user":
                    AddToolsForType<PublicTool>(toolCollection);
                    AddToolsForType<UserTool>(toolCollection);
                    break;
                default:
                    AddToolsForType<PublicTool>(toolCollection);
                    break;
            }
        }
    };
})
```

### 3. Authentication Logic
Simple authentication extracts user information from HTTP headers:

```csharp
static string GetUserRole(HttpContext context)
{
    // Check X-User-Role header or Authorization pattern
    // Returns "admin", "user", or "public"
}

static string GetUserId(HttpContext context)
{
    // Extract user ID from headers
    // Returns user identifier
}
```

### 4. Dynamic Tool Loading
A helper method recreates tool instances for the filtered collection:

```csharp
static void AddToolsForType<T>(McpServerPrimitiveCollection<McpServerTool> toolCollection)
{
    // Use reflection to find and recreate tools from the specified type
    // Add them to the session-specific tool collection
}
```

## Key Benefits

1. **Security**: Users only see tools they're authorized to use
2. **Scalability**: Per-session filtering doesn't affect other users
3. **Flexibility**: Easy to add new roles and permission levels
4. **Maintainability**: Clear separation between authentication and tool logic
5. **Performance**: Tools are filtered at session start, not per request

## Adapting for Production

To use this pattern in production:

1. **Replace header-based auth** with proper authentication (JWT, OAuth2, etc.)
2. **Add authorization policies** using ASP.NET Core's authorization framework
3. **Store permissions in database** instead of hardcoded role checks
4. **Add caching** for permission lookups to improve performance
5. **Implement proper logging** and monitoring for security events
6. **Add rate limiting** and other security measures

## Related Issues

- [#714](https://github.com/modelcontextprotocol/csharp-sdk/issues/714) - Support varying tools/resources per user
- [#222](https://github.com/modelcontextprotocol/csharp-sdk/issues/222) - Related per-user filtering discussion  
- [#237](https://github.com/modelcontextprotocol/csharp-sdk/issues/237) - Session-specific tool configuration
- [#476](https://github.com/modelcontextprotocol/csharp-sdk/issues/476) - Dynamic tool management
- [#612](https://github.com/modelcontextprotocol/csharp-sdk/issues/612) - Per-session resource filtering

## Learn More

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [ASP.NET Core MCP Integration](../../src/ModelContextProtocol.AspNetCore/README.md)
- [MCP C# SDK Documentation](https://modelcontextprotocol.github.io/csharp-sdk/)