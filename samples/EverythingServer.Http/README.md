# EverythingServer.Http

HTTP-based MCP server demonstrating all MCP capabilities using ASP.NET Core.

## Overview

This is the HTTP transport implementation of the EverythingServer sample. It demonstrates how to build an MCP server that communicates over HTTP with support for:

- Multiple concurrent sessions
- Resource subscriptions with per-session tracking
- Long-running operations with progress reporting
- Sampling (LLM integration)
- Logging level management
- OpenTelemetry integration

## Running the Server

```bash
dotnet run --project samples/EverythingServer.Http/EverythingServer.csproj
```

By default, the server runs on `http://localhost:5000` and `https://localhost:5001`.

You can configure the port and other settings in `appsettings.json` or via command-line arguments:

```bash
dotnet run --project samples/EverythingServer.Http/EverythingServer.csproj --urls "http://localhost:3000"
```

## Testing the Server

Use the included `EverythingServer.http` file with VS Code's REST Client extension or similar tools to test the endpoints.

## Architecture

This project uses:
- `EverythingServer.Core` for shared MCP handlers
- ASP.NET Core for HTTP hosting
- `WithHttpTransport()` with `RunSessionHandler` for session management
- Background services started per session for subscriptions and logging updates

## Key Features

- **Session Management**: Each HTTP session maintains its own subscription list
- **Resource Subscriptions**: Clients can subscribe to resources and receive periodic updates
- **Logging Messages**: Periodic logging messages at various levels based on server's current logging level
- **Complete MCP Implementation**: All MCP capabilities demonstrated in one server
