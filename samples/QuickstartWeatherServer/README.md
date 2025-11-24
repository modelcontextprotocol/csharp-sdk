# QuickstartWeatherServer Sample

This sample demonstrates how to create an MCP server that provides weather-related tools using the weather.gov API. This is a file-based program that runs without a traditional project file.

## Requirements

- .NET 8.0 SDK or later
- No project file required!

## Running the Sample

Simply run the Program.cs file directly:

```bash
dotnet run Program.cs
```

Or on Unix-like systems, make the file executable:

```bash
chmod +x Program.cs
./Program.cs
```

The server will start and listen for MCP messages on stdin/stdout (stdio transport).

## Available Tools

The server provides two weather tools:

1. **GetAlerts** - Get weather alerts for a US state (use 2-letter abbreviation like "NY")
2. **GetForecast** - Get weather forecast for a location (requires latitude and longitude)

## Testing the Server

You can test the server using the QuickstartClient or any MCP-compatible client:

```bash
# From the repository root
dotnet run --project samples/QuickstartClient samples/QuickstartWeatherServer
```

Or test with the MCP inspector:

```bash
npx @modelcontextprotocol/inspector dotnet run Program.cs
```

## What This Sample Shows

- Creating an MCP server using `Host.CreateApplicationBuilder`
- Registering tools with `WithTools<T>()`
- Using dependency injection to provide HttpClient to tools
- Configuring logging to stderr for MCP compatibility
- Using file-scoped classes for tool implementations

## Reference

- [File-Based Programs Tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/tutorials/file-based-programs)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/specification/)
- [Weather.gov API](https://www.weather.gov/documentation/services-web-api)
