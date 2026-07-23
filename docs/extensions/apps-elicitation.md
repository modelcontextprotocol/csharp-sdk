# MCP Apps as elicitation UI: prototype extension

This prototype composes core form elicitation, MCP Apps, and Multi Round-Trip Requests (MRTR) into one
interoperable flow. It is informed by ext-apps issue #511, discussion #514, PR #531, and the deferred-tool
workaround in PR #390.

## Capability negotiation

An MCP Apps host does not necessarily know how to route an elicitation to an app, manage its input-required
lifecycle, or fall back safely. This prototype adds an optional `elicitation` member to the existing MCP Apps
client capability:

```json
{
  "capabilities": {
    "elicitation": { "form": {} },
    "extensions": {
      "io.modelcontextprotocol/ui": {
        "mimeTypes": ["text/html;profile=mcp-app"],
        "elicitation": {}
      }
    }
  }
}
```

The `elicitation` member is experimental and does not claim adoption by the MCP project. Keeping it inside
`io.modelcontextprotocol/ui` avoids inventing extension dependency semantics and lets MCP Apps evolve additively.

## Elicitation request convention

The request remains a valid core form elicitation. The app link reuses MCP Apps metadata exactly as proposed in
issue #511:

```json
{
  "method": "elicitation/create",
  "params": {
    "mode": "form",
    "message": "Review the portfolio and confirm its manager.",
    "requestedSchema": {
      "type": "object",
      "properties": {
        "confirmed": { "type": "boolean" },
        "selectedManagerId": { "type": "string" }
      },
      "required": ["confirmed", "selectedManagerId"]
    },
    "_meta": {
      "ui": { "resourceUri": "ui://portfolio/assign-manager" }
    }
  }
}
```

A host supporting MCP Apps elicitation reads and renders the resource, then forwards `elicitation/create` to that app
as JSON-RPC after the normal `ui/initialize` / `ui/notifications/initialized` handshake. The app returns the
standard `ElicitResult`. This follows the direction explored by PR #531 while making app selection explicit.

A capability-aware server omits `_meta.ui` when the client has form elicitation but lacks MCP Apps elicitation, so
the client renders `requestedSchema` using its native form UI. A server that sends the optional hint unconditionally
remains compatible with clients that ignore unknown metadata. In both cases, the server receives the same core
`ElicitResult`.

## Stateless 2026-07-28 MRTR flow

```text
Host                  Stateless MCP server              MCP App
 | tools/call -----------------> |                         |
 | <--- input_required ----------|                         |
 |      elicitation/create + ui:// resource                |
 | resources/read -------------> |                         |
 | <--- text/html;profile=mcp-app|                         |
 | ui/initialize ----------------------------------------> |
 | <----------------------------- ui/notifications/initialized
 | elicitation/create ----------------------------------> |
 | <----------------------------- ElicitResult -----------|
 | tools/call + inputResponses ->|                         |
 | <--- final CallToolResult -----|                         |
```

The server cannot suspend an in-memory handler across stateless HTTP requests. The C# convention therefore uses
`InputRequiredException` on round one and deterministically re-runs the handler on round two. The original tool
arguments and opaque `requestState` must contain everything needed to resume safely. Implementations must avoid
performing non-idempotent work before the elicitation has resolved.

## C# API shape

- `WithMcpApps()` advertises the MCP Apps extension.
- `AddClientCapabilities(...)` advertises form elicitation plus the MCP Apps elicitation capability.
- `SetAppUi(...)` and `GetAppUi(...)` strongly type the `_meta.ui.resourceUri` convention.
- `SetAppUiIfSupported(...)` reads the request-scoped 2026-07-28 capabilities (or legacy session capabilities) and
  leaves the core request unchanged unless form elicitation and both app extensions were advertised.
- `ResolveOrRequest<T>(...)` emits the first-round MRTR request and deserializes the retried response as `T`.

## Host requirements and safety

- Validate the URI and only resolve declared `ui://` resources from the requesting server.
- Preserve the normal elicitation identity, review, decline, cancel, and notification behavior.
- Validate accepted content against `requestedSchema`; the app is not a trusted validator.
- Apply the complete MCP Apps sandbox, CSP, permissions, origin, and teardown rules.
- Do not use form mode for secrets or credentials; use core URL-mode elicitation for sensitive input.
- Bind pending elicitations to the originating server, user, request, and rendered app instance.
- Support sequential requests explicitly; concurrent routing needs stable per-elicitation app instances.

## Remaining spec questions

1. Should forwarding use the standard `elicitation/create` method, as PR #531 does, or a UI-prefixed method?
2. Should app support be declared in `ui/initialize` as the same first-class `elicitation` capability?
3. Should `_meta.ui.resourceUri` alone opt into routing, or must the MCP Apps elicitation capability be present?
4. Who performs final schema validation and how are invalid app responses surfaced without losing the elicitation?
5. What lifecycle notification tells the app and host that the elicitation has completed or been cancelled externally?
6. How should multiple simultaneous app elicitations from one tool call be ordered and displayed?
