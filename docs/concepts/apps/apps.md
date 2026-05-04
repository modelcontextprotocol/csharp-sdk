---
title: MCP Apps
author: mikekistler
description: How to use the MCP Apps extension to deliver interactive UIs from MCP servers.
uid: apps
---

# MCP Apps

[MCP Apps] is an extension to the Model Context Protocol that enables MCP servers to deliver interactive user interfaces — dashboards, forms, visualizations, and more — directly inside conversational AI clients.

[MCP Apps]: https://modelcontextprotocol.io/specification/draft/extensions/apps

> [!IMPORTANT]
> MCP Apps support is experimental. All types are marked with `[Experimental("MCPEXP003")]` and require suppressing that diagnostic to use.

## Installation

MCP Apps is provided in the `ModelContextProtocol.Extensions.Apps` package, which layers on top of the core SDK:

```shell
dotnet add package ModelContextProtocol.Extensions.Apps
```

## Overview

The MCP Apps extension introduces the concept of **UI resources** — HTML pages served by the MCP server that a client can display alongside the conversation. Tools can be associated with a UI resource so the client knows which interface to show when a tool is called.

The key concepts are:

- **UI capability negotiation** — Client and server declare support via `extensions["io.modelcontextprotocol/ui"]`
- **UI resources** — HTML content served with the MIME type `text/html;profile=mcp-app`
- **Tool UI metadata** — Tools declare their associated UI resource in `_meta.ui`

## Associating tools with UI resources

### Using the builder extension (recommended)

The simplest approach is to apply `[McpAppUi]` attributes to your tool methods and call `WithMcpApps()` on the server builder:

```csharp
[McpServerToolType]
public class WeatherTools
{
    [McpServerTool, Description("Get current weather for a location")]
    [McpAppUi(ResourceUri = "ui://weather/view.html")]
    public static string GetWeather(string location) => $"Weather for {location}";

    [McpServerTool, Description("Get forecast (model-only tool)")]
    [McpAppUi(ResourceUri = "ui://weather/forecast.html", Visibility = [McpUiToolVisibility.Model])]
    public static string GetForecast(string location) => $"Forecast for {location}";
}
```

```csharp
builder.Services.AddMcpServer()
    .WithTools<WeatherTools>()
    .WithMcpApps();
```

The `WithMcpApps()` call registers a post-configuration step that processes all registered tools and applies `[McpAppUi]` attribute metadata to their `_meta.ui` field automatically.

### Using the attribute with manual processing

If you create tools manually (without `WithMcpApps()`), you can still use the attribute and process tools explicitly:

```csharp
var tools = new[]
{
    McpServerTool.Create(typeof(WeatherTools).GetMethod(nameof(WeatherTools.GetWeather))!),
    McpServerTool.Create(typeof(WeatherTools).GetMethod(nameof(WeatherTools.GetForecast))!),
};

McpApps.ApplyAppUiAttributes(tools);
```

### Using the programmatic API

For full control, use `McpApps.SetAppUi` to set UI metadata directly:

```csharp
var tool = McpServerTool.Create((string location) => $"Weather for {location}");

McpApps.SetAppUi(tool, new McpUiToolMeta
{
    ResourceUri = "ui://weather/view.html",
    Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App],
});
```

## Checking client capabilities

During a session, you can check whether the connected client supports MCP Apps:

```csharp
[McpServerTool, Description("Get weather")]
[McpAppUi(ResourceUri = "ui://weather/view.html")]
public static string GetWeather(McpServer server, string location)
{
    var uiCapability = McpApps.GetUiCapability(server.ClientCapabilities);
    if (uiCapability is not null)
    {
        // Client supports MCP Apps — the UI will be displayed
    }

    return $"Weather for {location}";
}
```

## Tool visibility

The `Visibility` property controls which principals can invoke the tool:

| Value | Meaning |
| - | - |
| `McpUiToolVisibility.Model` | Only the LLM can call this tool |
| `McpUiToolVisibility.App` | Only the app UI can call this tool |
| Both (or null/empty) | Both the model and app can call the tool (default) |

## UI resources

UI resources are HTML pages registered with the MCP server using the `ui://` URI scheme and the `text/html;profile=mcp-app` MIME type. The `McpUiResourceMeta` type provides metadata for these resources, including:

- **CSP (Content Security Policy)** — Controls allowed origins for network requests and resource loads
- **Permissions** — Sandbox permissions (scripts, forms, popups, etc.)
- **Domain** — Dedicated origin for OAuth flows and CORS
- **PrefersBorder** — Whether the host should render a visual border

## Constants

The <xref:ModelContextProtocol.Server.McpApps> class provides constants for protocol values:

| Constant | Value | Usage |
| - | - | - |
| `McpApps.ResourceMimeType` | `text/html;profile=mcp-app` | MIME type for UI resources |
| `McpApps.ExtensionId` | `io.modelcontextprotocol/ui` | Key in `extensions` capability dictionary |

## Serialization

MCP Apps types use source-generated JSON serialization for Native AOT compatibility. Use `McpApps.SerializerOptions` when serializing extension types:

```csharp
var json = JsonSerializer.Serialize(toolMeta, McpApps.SerializerOptions);
var deserialized = JsonSerializer.Deserialize<McpUiToolMeta>(json, McpApps.SerializerOptions);
```
