# ASP.NET Core MCP Server with Routing

This sample demonstrates route-aware tool filtering in MCP servers, allowing different tool sets to be exposed at different HTTP endpoints based on the `[McpServerToolRoute]` attribute.

## Overview

The routing feature enables you to:

- Expose different tools at different HTTP endpoints
- Create context-specific tool collections (admin, utilities, etc.)
- Maintain global tools available on all routes
- Filter tool visibility based on the requested route

## Route Configuration

### Available Routes

| Route | Available Tools | Description |
|-------|----------------|-------------|
| `/mcp` (global) | All tools | Default route with complete tool set |
| `/mcp/admin` | Admin tools + Global tools | Administrative functions |
| `/mcp/weather` | Weather tools + Global tools | Weather-related operations |
| `/mcp/math` | Math tools + Global tools | Mathematical calculations |
| `/mcp/utilities` | Utility tools + Global tools | General utility functions |
| `/mcp/echo` | Echo tools + Global tools | Echo and text operations |

### Tool Categories

- **Global Tools**: `SampleLLM` (available on all routes)
- **Admin Tools**: `GetSystemStatus`, `RestartService` (admin route only)
- **Weather Tools**: `GetWeather`, `GetForecast` (weather + utilities routes)
- **Math Tools**: `Add`, `Factorial` (math + utilities routes)
- **Echo Tools**: `Echo`, `EchoAdvanced` (echo + utilities routes)

## Running the Sample

1. Start the server:
   ```bash
   cd samples/UrlRoutingSseServer
   dotnet run
   ```

2. The server will start at `http://localhost:5000` (or port shown in console)

## Testing Different Routes

You can test the routing behavior using curl or any HTTP client:

### List All Tools (Global Route)
```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### List Admin Tools Only
```bash
curl -X POST http://localhost:5000/mcp/admin \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### List Weather Tools Only
```bash
curl -X POST http://localhost:5000/mcp/weather \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### Call a Tool
```bash
curl -X POST http://localhost:5000/mcp/weather \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_weather","arguments":{"city":"Seattle"}}}'
```

## Expected Results

- **Global route** (`/mcp`): Returns all 9 tools
- **Admin route** (`/mcp/admin`): Returns 3 tools (2 admin + 1 global)
- **Weather route** (`/mcp/weather`): Returns 3 tools (2 weather + 1 global)
- **Math route** (`/mcp/math`): Returns 3 tools (2 math + 1 global)
- **Utilities route** (`/mcp/utilities`): Returns 4 tools (3 utility + 1 global)

## Implementation Details

### Key Configuration Changes

The routing feature requires two configuration changes:

```csharp
// Use routing-enabled transport
builder.Services.AddMcpServer()
    .WithHttpTransportAndRouting()  // Instead of .WithHttpTransport()
    
// Map with routing support
app.MapMcpWithRouting("mcp");       // Instead of app.MapMcp()
```

### Route Attribute Usage

```csharp
[McpServerTool, Description("Admin-only tool")]
[McpServerToolRoute("admin")]           // Single route
public static string AdminTool() { ... }

[McpServerTool, Description("Multi-route tool")]
[McpServerToolRoute("weather", "utilities")] // Multiple routes
public static string UtilityTool() { ... }

[McpServerTool, Description("Global tool")]
// No [McpServerToolRoute] = available everywhere
public static string GlobalTool() { ... }
```

## Use Cases

This routing feature enables scenarios like:

- **Multi-agent system coordination**: Different agent types access specialized tool sets (research agents get web search, execution agents get file operations)
- **Context-aware tool separation**: Specialized agents with distinct purposes and capabilities working within the same MCP server
- **Agent workflow orchestration**: Route-specific tools for different phases of multi-step agent workflows
- **Specialized agent environments**: Domain-specific agents (coding, research, planning) each with their appropriate toolset
- **Agent collaboration patterns**: Enabling agent-to-agent handoffs with context-appropriate tools at each stage

## Key Files

- `Program.cs`: Server configuration with routing enabled
- `Tools/AdminTool.cs`: Administrative tools (admin route only)
- `Tools/EchoTool.cs`: Basic echo tools with route filtering
- `Tools/MathTool.cs`: Mathematical calculation tools
- `Tools/SampleLlmTool.cs`: Global tool (no route restriction)
- `Tools/WeatherTool.cs`: Weather-related tools