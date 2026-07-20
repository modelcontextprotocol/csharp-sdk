# Stateless Show Planner

A small stdio MCP server built for Visual Studio that replaces suspended, server-stateful elicitation code with Multi Round-Trip Requests (MRTR).

## What it shows

- `plan_show` throws `InputRequiredException` for two form rounds.
- Visual Studio can present its current elicitation UI through the SDK compatibility resolver.
- Every retry carries an opaque Base64 `requestState`; the handler retains no conversation state.
- An MCP `2026-07-28` client uses the same code as native stateless MRTR.
- `surprise_me` asks the client's model for a concept through an MRTR sampling request.
- `unlock_grand_finale` adds a tool at runtime and triggers `notifications/tools/list_changed`.

The sampling feature is intentionally included to demonstrate Visual Studio's current approval UX even though MCP `2026-07-28` deprecates sampling.

## Visual Studio demo

Build once before Visual Studio starts the server:

```powershell
dotnet build samples\ShowPlannerServer\ShowPlannerServer.csproj
```

The repository `.mcp.json` registers **Stateless Show Planner**. Try this sequence:

1. Call `plan_show` for a developer conference closing session.
2. Complete the production-details form, then approve the summarized run sheet.
3. Call `surprise_me` and approve Visual Studio's sampling request.
4. Call `unlock_grand_finale`; reopen the tool picker and invoke the newly discovered `launch_grand_finale`.

## Migration pattern

The old stateful pattern awaits a callback while the handler remains alive:

```csharp
ElicitResult result = await server.ElicitAsync(...);
```

The MRTR pattern returns the input request and resumes from client-carried state:

```csharp
throw new InputRequiredException(
    inputRequests: new Dictionary<string, InputRequest> { /* form request */ },
    requestState: EncodeState(progress));
```

On retry, read `context.Params.InputResponses` and `context.Params.RequestState`. The SDK translates this into legacy `elicitation/create` calls for current-protocol stateful clients such as Visual Studio.
