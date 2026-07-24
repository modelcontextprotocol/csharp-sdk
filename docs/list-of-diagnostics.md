---
uid: list-of-diagnostics
---

# List of Diagnostics Produced by MCP C# SDK

This document provides information about each of the diagnostics produced by the MCP C# SDK analyzers and source generators.

## Analyzer Diagnostics

Analyzer diagnostic IDs are in the format `MCP###`.

| Diagnostic ID | Description                                                       |
|:--------------|:------------------------------------------------------------------|
| `MCP001`      | Invalid XML documentation for MCP method                          |
| `MCP002`      | MCP method must be partial to generate `[Description]` attributes |

## Experimental APIs

Experimental diagnostic IDs are in the format `MCPEXP###`.

As new functionality is introduced to this SDK, new in-development APIs are marked as being experimental. Experimental APIs offer no compatibility guarantees and can change without notice. They are usually published in order to gather feedback before finalizing a design.

You may use experimental APIs in your application, but we advise against using these APIs in production scenarios as they may not be fully tested nor fully reliable. Additionally, we strongly recommend that library authors do not publish versions of their libraries that depend on experimental APIs as this will quite possibly lead to future breaking changes and diamond problems.

If you use experimental APIs, you will get one of the diagnostics shown below. The diagnostic is there to let you know you're using such an API so that you can avoid accidentally depending on experimental features. You can suppress these diagnostics if desired.

| Diagnostic ID | Description |
| :------------ | :---------- |
| `MCPEXP001` | Experimental APIs tied to MCP specification features. Reuse this ID for newly introduced experimental spec features, and add feature-specific messages/URLs in `Experimentals`. |
| `MCPEXP002` | Experimental SDK APIs unrelated to the MCP specification itself, including subclassing `McpClient`/`McpServer` (see [#1363](https://github.com/modelcontextprotocol/csharp-sdk/pull/1363)) and `RunSessionHandler`, which may be removed or change signatures in a future release (consider using `ConfigureSessionOptions` instead). |
| `MCPEXP003` | Experimental MCP Apps extension APIs. MCP Apps is the first official MCP extension (`io.modelcontextprotocol/ui`), enabling servers to deliver interactive UIs inside AI clients (see [MCP Apps specification](https://github.com/modelcontextprotocol/ext-apps/blob/main/specification/2026-01-26/apps.mdx)). |

## Obsolete APIs

Obsolete diagnostic IDs are in the format `MCP9###`.

When APIs are marked as obsolete, a diagnostic is emitted to warn users that the API will be removed in a future version. Diagnostic IDs are never reused, even after an obsolete API has been removed, to avoid suppressing warnings for different APIs.

| Diagnostic ID | Status | Description |
| :------------ | :----- | :---------- |
| `MCP9001` | In place | The `EnumSchema` and `LegacyTitledEnumSchema` APIs are deprecated as of specification version 2025-11-25. Use the current schema APIs instead. |
| `MCP9002` | Removed | The `AddXxxFilter` extension methods on `IMcpServerBuilder` (for example, `AddListToolsFilter`, `AddCallToolFilter`, `AddIncomingMessageFilter`) were superseded by `WithRequestFilters()` and `WithMessageFilters()`. |
| `MCP9003` | In place | The `RequestContext<TParams>(McpServer, JsonRpcRequest)` constructor is obsolete. Use the overload that accepts a `parameters` argument: `RequestContext<TParams>(McpServer, JsonRpcRequest, TParams)`. |
| `MCP9004` | In place | <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions.EnableLegacySse> opts into the legacy SSE transport which has no built-in HTTP-level backpressure. Use Streamable HTTP instead. See [Stateless — Legacy SSE transport](xref:stateless#legacy-sse-transport) for details. |
| `MCP9005` | In place | The Roots, Sampling, and Logging features are deprecated as of specification version 2026-07-28 and may be removed in a future version. See SEP-2577 for more information. |
| `MCP9006` | In place | The stateful Streamable HTTP configuration knobs on <xref:ModelContextProtocol.AspNetCore.HttpServerTransportOptions> — `EventStreamStore`, `SessionMigrationHandler`, `PerSessionExecutionContext`, `IdleTimeout`, and `MaxIdleSessionCount` — only apply when `Stateless = false`. Starting with the `2026-07-28` protocol revision, Streamable HTTP no longer supports sessions, and the SDK now defaults `Stateless` to `true`. These knobs remain available for back-compat with the legacy stateful Streamable HTTP transport but new code should target the stateless path. |
| `MCP9007` | In place | `AuthorizationRedirectDelegate` and `ClientOAuthOptions.AuthorizationRedirectDelegate` are retained for source and binary compatibility but cannot provide the RFC 9207 authorization-response issuer. Use `ClientOAuthOptions.AuthorizationCallbackHandler` for issuer-aware authorization flows. |
