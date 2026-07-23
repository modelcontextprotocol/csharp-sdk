# MCP App as custom elicitation UI

This sample is a minimal host + server implementation of the composition proposed in
[ext-apps#511](https://github.com/modelcontextprotocol/ext-apps/issues/511).

See [the prototype extension design](../../docs/extensions/apps-elicitation.md) for the proposed wire contract,
fallback behavior, safety requirements, and open specification questions.

The server is deliberately stateless and pins the draft `2026-07-28` protocol. The flow is:

1. The host calls `assign_account_manager`.
2. The server throws `InputRequiredException` with an `elicitation/create` input request.
3. The request retains the normal `requestedSchema` and adds `_meta.ui.resourceUri` only when the requesting client
   advertised form elicitation, MCP Apps, and the app-elicitation extension.
4. The host reads the `ui://` MCP App resource and performs the Apps `ui/initialize` handshake.
5. The host forwards `elicitation/create` to the iframe as JSON-RPC.
6. The app returns `ElicitResult`; the C# client places it in `inputResponses` and retries the tool.
7. The stateless tool deserializes the response as `ManagerAssignment` and completes.

A client that advertises only core form elicitation receives the same `requestedSchema` without `_meta.ui`, renders
its native form, and completes the identical MRTR retry.

Run in two terminals:

```bash
dotnet run --project samples/AppElicitationServer
dotnet run --project samples/AppElicitationHost
```

Then open <http://localhost:5200> and choose **Run assign_account_manager**.

The browser host is intentionally small. It demonstrates the proposed lifecycle and wire shape, but it is not a
general-purpose MCP Apps host or a substitute for the ext-apps sandbox proxy implementation.
