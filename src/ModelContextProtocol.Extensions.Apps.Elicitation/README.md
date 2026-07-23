# MCP Apps Elicitation prototype

This experimental package composes three negotiated features:

- core form elicitation;
- `io.modelcontextprotocol/ui` (MCP Apps);
- `io.modelcontextprotocol/ui-elicitation` (this prototype).

An elicitation keeps its normal `requestedSchema` fallback and adds the MCP Apps resource link proposed in
modelcontextprotocol/ext-apps#511:

```json
"_meta": { "ui": { "resourceUri": "ui://portfolio/assign-manager" } }
```

`McpAppElicitation.ResolveOrRequest<T>` implements the explicit MRTR convention required by a stateless
2026-07-28 server: the first invocation returns `InputRequiredResult`; the client resolves the app UI and retries;
the second invocation deserializes the matching `inputResponses` entry into `T`.

Use `SetAppUiIfSupported(request, context, resourceUri)` in a tool to read the authoritative request-scoped
capabilities on 2026-07-28. Clients that advertise core form elicitation without both app extensions receive the
same request without `_meta.ui` and render `requestedSchema` using their native elicitation UI.

This is a reference implementation for discussion, not an adopted MCP extension.
