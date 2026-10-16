# ASP.NET Core MCP client with per-user sessions

This sample shows an ASP.NET Core Web API acting as an MCP client while keeping one MCP connection per application user session. It is intended for applications that need session continuity for server-to-client features such as elicitation and progress notifications.

The sample deliberately separates the **application session** from the MCP protocol session. `SessionClientRegistry<TClient>` owns the mapping and provides:

- lazy, single initialization of a client for each application session;
- serialization of operations within one session while allowing different sessions to run concurrently;
- explicit session removal and deterministic asynchronous disposal;
- automatic removal of idle sessions; and
- cleanup of every remaining client during application shutdown.

## Run the sample

Start an HTTP MCP server, such as `AspNetCoreMcpServer`, then run this project:

```bash
dotnet run --project samples/AspNetCoreMcpServer
dotnet run --project samples/AspNetCoreMcpClient
```

The default MCP endpoint is `http://localhost:3001`. Change `McpServer:Endpoint` in `appsettings.json` when needed.

The HTTP examples use `X-Demo-User` solely to make session reuse visible without adding an authentication system:

```bash
curl -H "X-Demo-User: alice" http://localhost:5000/tools

curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Demo-User: alice" \
  -d '{"message":"hello"}' \
  http://localhost:5000/tools/echo

curl -X DELETE -H "X-Demo-User: alice" http://localhost:5000/session
```

Use the URL printed by `dotnet run` if it differs from port 5000.

## Elicitation flow

When the MCP server sends an elicitation request during a tool call, `ElicitationBroker` holds that request while the application's frontend collects an answer:

1. The frontend polls `GET /elicitation` with the same application-session identity.
2. A `200` response contains the pending request and its ID; `204` means there is no pending request.
3. The frontend posts an `ElicitResult` to `POST /elicitation/{requestId}`.
4. The original tool call resumes and returns its response.

The broker intentionally permits one pending elicitation per application session because the registry serializes that session's MCP operations.

Because the registry waits for a session's in-flight operation before disposing its client, a tool call blocked on an unanswered elicitation would otherwise stall `DELETE /session` and application shutdown. `DELETE /session` cancels the session's pending elicitation first, and the sample registers an `ApplicationStopping` callback that cancels all pending elicitations before the registry is disposed.

## Production considerations

- **Never trust a caller-provided session header.** Replace `X-Demo-User` with a key derived from authenticated, server-side identity or session state. Do not use access tokens or other secrets as dictionary keys.
- The registry is in-memory and therefore single-node. For multiple application instances, use sticky routing so one user's requests reach the owning process, or implement distributed ownership and session resumption. A distributed cache alone cannot store a live `McpClient` connection.
- Choose an idle timeout that fits both application behavior and upstream resource limits. Explicitly remove the session at logout when possible.
- Operations are serialized per user to protect application-level session state. If your use case permits concurrent MCP requests, adjust the registry policy rather than creating duplicate clients.
- The sample collects progress updates for a compact response. A real frontend would normally stream them with Server-Sent Events, WebSockets, or another application channel.
