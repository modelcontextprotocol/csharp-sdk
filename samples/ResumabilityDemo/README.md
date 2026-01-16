# ResumabilityDemo

A sample application for testing MCP streamable HTTP **resumability**, **redelivery**, and **SSE polling mode** using various `IDistributedCache` implementations.

## Overview

This sample demonstrates:

1. **Resumability** - Events are stored in a distributed cache and replayed when clients reconnect with `Last-Event-ID`
2. **Redelivery** - Missed events during disconnection are delivered when the client reconnects
3. **Polling Mode** - Server-initiated disconnect via `EnablePollingAsync()` that switches from streaming to polling

## Architecture

```
┌─────────────────────┐      ┌─────────────────────┐
│  ResumabilityDemo   │      │  ResumabilityDemo   │
│      Client         │◄────►│      Server         │
└─────────────────────┘      └─────────┬───────────┘
                                       │
                             ┌─────────▼───────────┐
                             │  IDistributedCache  │
                             │  (Memory/Redis/SQL) │
                             └─────────────────────┘
```

## Quick Start

### 1. Run with In-Memory Cache (Default)

```bash
# Terminal 1: Start the server
cd samples/ResumabilityDemo/ResumabilityDemo.Server
dotnet run

# Terminal 2: Start the client
cd samples/ResumabilityDemo/ResumabilityDemo.Client
dotnet run
```

### 2. Run with Redis

```bash
# Start Redis and RedisInsight (GUI)
cd samples/ResumabilityDemo
docker compose up redis redisinsight -d

# Update appsettings.json or use environment variables
# Terminal 1: Start the server with Redis
cd ResumabilityDemo.Server
dotnet run -- --CacheProvider=Redis --Redis:Configuration=localhost:6379

# Terminal 2: Start the client
cd ResumabilityDemo.Client
dotnet run
```

#### Browsing Redis with RedisInsight

RedisInsight is a GUI for browsing Redis contents. After starting the containers:

1. Open http://localhost:5540 in your browser
2. Click "Add Redis Database"
3. Enter connection details:
   - **Host**: `redis` (or `host.docker.internal` if that doesn't work)
   - **Port**: `6379`
   - **Name**: Any friendly name like "MCP Resumability"

You can then browse the keys stored by `DistributedCacheEventStreamStore`:
- `mcp:sse:v1:meta:{sessionId}:{streamId}` - Stream metadata
- `mcp:sse:v1:event:{eventId}` - Individual SSE events

### 3. Run with SQL Server

```bash
# Start SQL Server and initialize the cache table
cd samples/ResumabilityDemo
docker compose up sqlserver sqlserver-init -d

# Wait for initialization to complete
docker compose logs -f sqlserver-init

# Terminal 1: Start the server with SQL Server
cd ResumabilityDemo.Server
dotnet run -- --CacheProvider=SqlServer

# Terminal 2: Start the client
cd ResumabilityDemo.Client
dotnet run
```

## Available Tools

The server provides these tools for testing resumability:

| Tool | Description |
|------|-------------|
| `Echo` | Basic connectivity test - returns immediately |
| `DelayedEcho` | Delays before returning - test what happens if client disconnects mid-operation |
| `ProgressDemo` | Sends progress notifications at intervals - test resuming mid-stream |
| `TriggerPollingMode` | **Calls `EnablePollingAsync()`** - the key tool for testing server-side disconnect |
| `ProgressThenPolling` | Combines streaming progress with polling transition |
| `GenerateUniqueId` | Returns a unique ID - verify same response on reconnection |

## Server Options

| Option | Description |
|--------|-------------|
| `--no-store` | Disable the event stream store (no resumability). Useful for comparison testing to see behavior without resumability support. |
| `--CacheProvider=<type>` | Set the cache provider: `Memory` (default), `Redis`, or `SqlServer` |

Example:
```bash
# Run without resumability support (for comparison)
dotnet run -- --no-store

# Run with Redis cache
dotnet run -- --CacheProvider=Redis --Redis:Configuration=localhost:6379
```

## Testing Scenarios

### Scenario 1: Basic Resumability

1. Connect the client: `connect`
2. Call a slow tool: `delay "Hello" 10`
3. While waiting, run `kill`
4. Observe that the response is still received (redelivered)

### Scenario 2: Polling Mode (Server-Side Disconnect)

1. Connect the client: `connect`
2. Call the polling tool: `polling 2 5`
   - Server will disconnect after establishing the stream
   - Client will poll every 2 seconds
   - After 5 seconds of "work", the result is available
3. Observe the client successfully receives the result via polling

### Scenario 3: Progress + Polling

1. Connect the client: `connect`
2. Call the combo tool: `combo 5 500 1 3`
   - 5 progress updates at 500ms intervals (streaming)
   - Then switch to polling with 1s retry
   - 3 seconds of work before result is ready
3. Observe progress updates, then polling for final result

### Scenario 4: Multi-Instance Testing

To verify true distributed cache behavior:

```bash
# Terminal 1: Server instance 1
cd ResumabilityDemo.Server
dotnet run --urls=http://localhost:5001

# Terminal 2: Server instance 2
cd ResumabilityDemo.Server
dotnet run --urls=http://localhost:5002

# Use a load balancer (e.g., nginx, HAProxy) or manually switch between instances
# Events stored by instance 1 can be read by instance 2
```

## Configuration

### appsettings.json

```json
{
  "CacheProvider": "Memory",  // "Memory", "Redis", or "SqlServer"
  
  "Redis": {
    "Configuration": "localhost:6379"
  },
  
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=McpCache;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True",
    "SchemaName": "dbo",
    "TableName": "SseEventCache"
  },
  
  "EventStreamStore": {
    "EventSlidingExpirationMinutes": 30,
    "EventAbsoluteExpirationHours": 2,
    "MetadataSlidingExpirationHours": 1,
    "MetadataAbsoluteExpirationHours": 4,
    "PollingIntervalMilliseconds": 100
  }
}
```

### Environment Variables

You can also use environment variables:

```bash
export CacheProvider=Redis
export Redis__Configuration=localhost:6379
```

## Client Commands

| Command | Description |
|---------|-------------|
| `connect` | Connect to the MCP server |
| `disconnect` | Disconnect from the server |
| `tools` | List available tools |
| `echo <msg>` | Call Echo tool |
| `delay <msg> [seconds]` | Call DelayedEcho (default: 5s) |
| `progress [steps] [intervalMs]` | Call ProgressDemo (default: 10, 1000ms) |
| `polling [retry] [work]` | Call TriggerPollingMode (default: 2s, 5s) |
| `combo [p] [i] [r] [w]` | Call ProgressThenPolling |
| `uid [prefix]` | Call GenerateUniqueId |
| `quit` or `exit` | Exit the client |

## Manual Testing with curl

```bash
# Initialize a session
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","method":"initialize","id":1,"params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1.0"}}}'

# Call the echo tool (use the session ID from above)
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -H "Mcp-Session-Id: <session-id>" \
  -d '{"jsonrpc":"2.0","method":"tools/call","id":2,"params":{"name":"Echo","arguments":{"message":"Hello"}}}'
```

## How DistributedCacheEventStreamStore Works

1. **Event Storage**: Each SSE event is stored with a unique ID containing session, stream, and sequence info
2. **Metadata Tracking**: Stream state (mode, completion, last sequence) is stored separately
3. **Resumption**: When a client reconnects with `Last-Event-ID`, events after that sequence are replayed
4. **Polling Mode**: When `EnablePollingAsync()` is called, the stream mode changes and the HTTP response ends

### Event ID Format

Event IDs are base64-encoded strings containing:
- Session ID
- Stream ID  
- Sequence number

This allows the store to efficiently locate and replay events from any point.

## Troubleshooting

### Redis Connection Issues

```bash
# Check Redis is running
docker compose ps redis
docker compose logs redis

# Test connectivity
redis-cli ping
```

### SQL Server Connection Issues

```bash
# Check SQL Server is running
docker compose ps sqlserver
docker compose logs sqlserver

# Verify table exists
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourStrong@Passw0rd" -C \
  -Q "USE McpCache; SELECT * FROM sys.tables;"
```

### Debug Logging

Set `Logging:LogLevel:ModelContextProtocol` to `Trace` in appsettings.json for detailed protocol logs.

## Related Documentation

- [MCP Specification - Streamable HTTP Transport](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http)
- [ASP.NET Core Distributed Caching](https://learn.microsoft.com/aspnet/core/performance/caching/distributed)
