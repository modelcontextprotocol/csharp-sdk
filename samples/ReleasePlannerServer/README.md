# Stateless Release Planner

A small stdio MCP server built for Visual Studio that replaces suspended, server-stateful elicitation code with Multi Round-Trip Requests (MRTR).

## What it shows

- `plan_release` throws `InputRequiredException` for release controls and approval.
- Visual Studio can present its current elicitation UI through the SDK compatibility resolver.
- Every retry carries an opaque Base64 `requestState`; the handler retains no conversation state.
- An MCP `2026-07-28` client uses the same code as native stateless MRTR.
- `unlock_production_deploy` adds a simulated deployment tool at runtime and triggers `notifications/tools/list_changed`.

## Visual Studio demo

Build once before Visual Studio starts the server:

```powershell
dotnet build samples\ReleasePlannerServer\ReleasePlannerServer.csproj
```

The repository `.mcp.json` registers **Stateless Release Planner**. Try this sequence:

1. Ask: `Use plan_release to prepare version 2.0.0 of Contoso.Api for production.`
2. Choose `production`, `canary`, `10` percent initial rollout, and a `1` percent rollback threshold.
3. Approve the summarized release plan and point out the final "Stateless proof" line.
4. Call `unlock_production_deploy`; reopen the tool picker and invoke the newly discovered `deploy_release`.

`deploy_release` is deliberately simulated. It demonstrates live capability discovery and destructive-tool consent without touching an environment.

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

The sample treats decoded state as untrusted input and validates its fields. A production server should also integrity-protect client-carried state when it contains authorization-sensitive data.
