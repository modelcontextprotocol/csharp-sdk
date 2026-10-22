# In-Memory Transport Sample

Demonstrates connecting an MCP client and server in the same process using stream-based
transports over in-memory pipes (`System.IO.Pipelines`) — no child process, no network.

The server is created directly with `McpServer.Create`, without a host or dependency
injection container, and exposes a single `Echo` tool defined from a delegate with
`McpServerTool.Create`. The client connects over the same pipe pair with
`StreamClientTransport`, lists the server's tools, and invokes the tool.

This pattern is useful for testing MCP servers, embedding a server inside a larger
application, or running a client and server in the same process without transport overhead.
See [Transports: In-memory transport](../../docs/concepts/transports/transports.md) for more
background.

## Run

```bash
dotnet run --project samples/InMemoryTransport/InMemoryTransport.csproj
```

Expected output:

```
Tool Name: Echo

Echo: Hello World
```

## Key files

- [`Program.cs`](Program.cs) — the entire sample: pipe setup, server creation, client
  connection, tool listing, and tool invocation.

## Notes

- `StreamServerTransport` and `StreamClientTransport` work with any `Stream`. This sample
  wires them to a pair of `Pipe` instances, one per direction; the client's output stream is
  the server's input stream and vice versa.
- The server is started with a fire-and-forget `server.RunAsync()` because both endpoints
  live in the same process. `await using` on the server and client ensures both are disposed
  when the program exits.
