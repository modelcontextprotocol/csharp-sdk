# ASP.NET Core MCP Server with Hybrid Protocol Routing

This sample demonstrates a workaround for serving both MCP protocol eras on one
Streamable HTTP endpoint with the C# SDK 2.0.0:

| Client | `/mcp` behavior |
|---|---|
| `2026-07-28` and later | Stateless, with native Multi Round-Trip Requests (MRTR) |
| `2025-11-25` and earlier | Stateful, with `Mcp-Session-Id` and legacy server-to-client requests |

A normally configured stateful server asks a dual-path modern client to fall back to
an initialize-era protocol. Use this workaround only when modern clients need to retain
the stateless `2026-07-28` protocol while legacy clients still need stateful features
such as `elicitation/create`.

## Run the sample

```bash
dotnet run --project samples/AspNetCoreMcpHybridServer
```

Connect clients to the `/mcp` endpoint at the URL printed by ASP.NET Core.

Call the `greet_with_confirmation` tool with a `name`. The same tool implementation:

- returns an `InputRequiredResult` to a `2026-07-28` client and resumes through MRTR;
- is automatically bridged by the SDK to `elicitation/create` for an initialize-era
  client on a stateful session.

The tool also receives `GreetingService` from dependency injection. This demonstrates
that application services do not need to be copied into the secondary container.

## How it works

`HttpServerTransportOptions` is consumed by a singleton transport handler, so one
service provider cannot configure both stateful and stateless handlers. The sample
therefore builds a small secondary service provider containing a stateless MCP HTTP
transport.

Middleware before the main application's `UseRouting()` checks
`MCP-Protocol-Version`. Modern `POST /mcp` requests run through the secondary stateless
pipeline. Other requests continue to the normal stateful `MapMcp("/mcp")` endpoint.

The secondary provider reuses the main provider's `IOptions<McpServerOptions>` and
`IOptionsFactory<McpServerOptions>`. Tools and handlers are consequently configured
once, while stateless requests still receive fresh `McpServerOptions` instances.
During stateless request processing, the SDK uses the original
`HttpContext.RequestServices` to resolve user-defined handler dependencies.

## Important limitations

This is a temporary composition technique, not a first-class hybrid transport API. It
depends on the SDK 2.0.0 stateless handler resolving application dependencies from
`HttpContext.RequestServices`.

The secondary container owns only transport infrastructure. Services used directly by
custom transport configuration, such as an event store or session callback, must be
forwarded or configured separately.

This sample is intentionally anonymous. In a real application, apply equivalent
authorization, CORS, rate limiting, endpoint metadata, and other security policies to
both request pipelines. The custom dispatcher is limited to `POST /mcp`; update its
path check if the MCP endpoint is mounted elsewhere.

MRTR tools must resume entirely from the original request arguments, `requestState`,
and `inputResponses`. Do not rely on in-memory handler state surviving between rounds.
