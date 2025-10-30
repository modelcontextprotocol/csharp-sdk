# JSON Serialization Configuration for MCP Server

This document demonstrates how to configure JSON serialization options for your MCP server to handle special cases like `double.Infinity` or `NaN` values.

## Problem

By default, JSON serialization in .NET doesn't support special floating-point values like positive/negative infinity and NaN. When a tool returns such values, you would get an error:

```
System.ArgumentException: .NET number values such as positive and negative infinity cannot be written as valid JSON. 
To make it work when using 'JsonSerializer', consider specifying 'JsonNumberHandling.AllowNamedFloatingPointLiterals'
```

## Solution

Configure server-wide JSON serialization options when setting up your MCP server:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Configure server-wide JSON serialization options
builder.Services.AddMcpServer(options =>
{
    options.JsonSerializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
})
.WithTools<MyTools>();

var app = builder.Build();
app.Run();

[McpServerToolType]
public class MyTools
{
    [McpServerTool]
    public static double[] GetSpecialNumbers()
    {
        // These values will now serialize correctly as "Infinity", "-Infinity", and "NaN"
        return new[] { double.PositiveInfinity, double.NegativeInfinity, double.NaN };
    }
}
```

## How It Works

1. The `JsonSerializerOptions` property on `McpServerOptions` provides server-wide default serialization settings
2. All tools, prompts, and resources registered via `WithTools*`, `WithPrompts*`, and `WithResources*` will use these options by default
3. Individual registrations can still override with their own specific options if needed

## Override for Specific Tools

If you need different serialization options for specific tools, you can still provide them explicitly:

```csharp
var customOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
};

builder.Services.AddMcpServer(options =>
{
    options.JsonSerializerOptions = McpJsonUtilities.DefaultOptions; // Default for most tools
})
.WithTools<MyTools>() // Uses server-wide options
.WithTools<SpecialTools>(customOptions); // Uses custom options
```

## Additional Configuration Options

You can configure other JSON serialization settings as needed:

```csharp
options.JsonSerializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
{
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    WriteIndented = true // For debugging
};
```
