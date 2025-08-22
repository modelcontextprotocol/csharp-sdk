# ASP.NET Core MCP Server with Per-Session Tool Filtering

This sample demonstrates how to create an MCP (Model Context Protocol) server that provides different sets of tools based on route-based session configuration. This showcases the technique of using `ConfigureSessionOptions` to dynamically modify the `ToolCollection` based on route parameters for each MCP session.

## Overview

The sample demonstrates route-based tool filtering using the MCP SDK's `ConfigureSessionOptions` callback. Instead of using authentication headers, this approach uses URL routes to determine which tools are available to each MCP session, making it easy to test different tool configurations.

## Features

- **Route-Based Tool Filtering**: Different routes expose different tool sets
- **Three Tool Categories**: 
  - **Clock**: Time and date related tools (`/clock`)
  - **Calculator**: Mathematical calculation tools (`/calculator`)
  - **UserInfo**: Session and system information tools (`/userinfo`)
- **Dynamic Tool Loading**: Tools are filtered per session based on the route used to connect
- **Easy Testing**: Simple URL-based testing without complex authentication setup
- **Comprehensive Logging**: Logs session configuration and tool access for monitoring

## Tool Categories

### Clock Tools (`/clock`)
- **GetTime**: Gets the current server time
- **GetDate**: Gets the current date in various formats
- **ConvertTimeZone**: Converts time between timezones (simulated)

### Calculator Tools (`/calculator`)
- **Calculate**: Performs basic arithmetic operations (+, -, *, /)
- **CalculatePercentage**: Calculates percentage of a number
- **SquareRoot**: Calculates square root of a number

### UserInfo Tools (`/userinfo`)
- **GetSessionInfo**: Gets information about the current MCP session
- **GetSystemInfo**: Gets system information about the server
- **EchoWithContext**: Echoes messages with session context
- **GetConnectionInfo**: Gets basic connection information

## Route-Based Configuration

The server uses route parameters to determine which tools to make available:

- `GET /clock` - MCP server with only clock/time tools
- `GET /calculator` - MCP server with only calculation tools  
- `GET /userinfo` - MCP server with only session/system info tools
- `GET /all` or `GET /` - MCP server with all tools (default)

## Running the Sample

1. Navigate to the sample directory:
   ```bash
   cd samples/AspNetCoreMcpPerSessionTools
   ```

2. Run the server:
   ```bash
   dotnet run
   ```

3. The server will start on `https://localhost:5001` (or the port shown in the console)

## Testing Tool Categories

### Testing Clock Tools
Connect your MCP client to: `https://localhost:5001/clock`
- Available tools: GetTime, GetDate, ConvertTimeZone

### Testing Calculator Tools  
Connect your MCP client to: `https://localhost:5001/calculator`
- Available tools: Calculate, CalculatePercentage, SquareRoot

### Testing UserInfo Tools
Connect your MCP client to: `https://localhost:5001/userinfo`  
- Available tools: GetSessionInfo, GetSystemInfo, EchoWithContext, GetConnectionInfo

### Testing All Tools
Connect your MCP client to: `https://localhost:5001/all` or `https://localhost:5001/`
- Available tools: All tools from all categories

### Browser Testing
You can also test the route detection in a browser:
- `https://localhost:5001/` - Shows available endpoints
- `https://localhost:5001/test-category/clock` - Tests clock category detection
- `https://localhost:5001/test-category/calculator` - Tests calculator category detection
- `https://localhost:5001/test-category/userinfo` - Tests userinfo category detection

## How It Works

### 1. Tool Registration
All tools are registered during startup using the normal MCP tool registration:

```csharp
builder.Services.AddMcpServer()
    .WithTools<ClockTool>()
    .WithTools<CalculatorTool>()
    .WithTools<UserInfoTool>();
```

### 2. Route-Based Session Filtering
The key technique is using `ConfigureSessionOptions` to modify the tool collection per session based on the route:

```csharp
.WithHttpTransport(options =>
{
    options.ConfigureSessionOptions = async (httpContext, mcpOptions, cancellationToken) =>
    {
        var toolCategory = GetToolCategoryFromRoute(httpContext);
        var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
        
        if (toolCollection != null)
        {
            // Clear all tools and add back only those for this category
            toolCollection.Clear();
            
            switch (toolCategory?.ToLower())
            {
                case "clock":
                    AddToolsForType<ClockTool>(toolCollection);
                    break;
                case "calculator":
                    AddToolsForType<CalculatorTool>(toolCollection);
                    break;
                case "userinfo":
                    AddToolsForType<UserInfoTool>(toolCollection);
                    break;
                default:
                    // All tools for default/all category
                    AddToolsForType<ClockTool>(toolCollection);
                    AddToolsForType<CalculatorTool>(toolCollection);
                    AddToolsForType<UserInfoTool>(toolCollection);
                    break;
            }
        }
    };
})
```

### 3. Route Parameter Detection
The `GetToolCategoryFromRoute` method extracts the tool category from the URL route:

```csharp
static string? GetToolCategoryFromRoute(HttpContext context)
{
    if (context.Request.RouteValues.TryGetValue("toolCategory", out var categoryObj) && categoryObj is string category)
    {
        return string.IsNullOrEmpty(category) ? "all" : category;
    }
    return "all"; // Default
}
```

### 4. Dynamic Tool Loading
The `AddToolsForType<T>` helper method uses reflection to discover and add all tools from a specific tool type to the session's tool collection.

## Key Benefits

- **Easy Testing**: No need to manage authentication tokens or headers
- **Clear Separation**: Each tool category is isolated and can be tested independently  
- **Flexible Architecture**: Easy to add new tool categories or modify existing ones
- **Production Ready**: The same technique can be extended for production scenarios with proper routing logic
- **Observable**: Built-in logging shows exactly which tools are configured for each session

## Adapting for Production

For production use, you might want to:

1. **Add Authentication**: Combine route-based filtering with proper authentication
2. **Database-Driven Categories**: Load tool categories and permissions from a database
3. **User-Specific Routing**: Use user information to determine allowed categories
4. **Advanced Routing**: Support nested categories or query parameters
5. **Rate Limiting**: Add rate limiting per tool category
6. **Caching**: Cache tool collections for better performance

## Related Issues

- [#714](https://github.com/modelcontextprotocol/csharp-sdk/issues/714) - Support varying tools/resources per user
- [#237](https://github.com/modelcontextprotocol/csharp-sdk/issues/237) - Session-specific tool configuration  
- [#476](https://github.com/modelcontextprotocol/csharp-sdk/issues/476) - Dynamic tool management
- [#612](https://github.com/modelcontextprotocol/csharp-sdk/issues/612) - Per-session resource filtering

## Learn More

- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [ASP.NET Core MCP Integration](../../src/ModelContextProtocol.AspNetCore/README.md)
- [MCP C# SDK Documentation](https://modelcontextprotocol.github.io/csharp-sdk/)