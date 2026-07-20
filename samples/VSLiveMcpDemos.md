# Visual Studio MCP Show Demos

These three small servers form one show-production story while covering a wide range of C# SDK and Visual Studio MCP features. Visual Studio is the client; there are no sample client projects.

| Demo | Stage moment | MCP features |
| --- | --- | --- |
| [Fireworks Server](FireworksServer/README.md) | One tool call launches synchronized fireworks in an MCP App and every browser in the room. | Tools, structured content, MCP Apps, prompts, resource templates, server instructions, stdio, stateless HTTP |
| [Stateless Show Planner](ShowPlannerServer/README.md) | Visual Studio shows familiar forms while the handler uses client-carried MRTR state; a new finale tool appears live. | MRTR, elicitation compatibility, sampling, `tools/list_changed`, dynamic tools |
| [Task-Aware Show Producer](ShowProducerServer/README.md) | The same tool runs inline today and becomes pollable, interactive, and cancellable for a Tasks-aware host. | Tasks extension, input-required tasks, polling, cancellation, inline fallback |

## One-time setup

Use Visual Studio 2026 18.7 or later, open `ModelContextProtocol.slnx`, and build the solution in the default **Debug** configuration:

```powershell
dotnet build
```

The checked-in `.mcp.json` points Visual Studio at the resulting assemblies. In the MCP server manager, trust and start the three show servers. Rebuild after changing a sample, then save `.mcp.json` to make Visual Studio restart the processes.

## Suggested ten-minute stage flow

1. Open <http://localhost:5399> in one or more browser windows. For physical audience devices, enable the opt-in LAN binding documented in the Fireworks README. Add the **choreograph_fireworks** prompt in Visual Studio, enter an occasion, and approve `launch_fireworks`. The MCP App and all connected browsers use the same structured show payload.
2. Invoke `plan_show`. Complete the details and approval forms, then point out the final "stateless proof" line. Invoke `surprise_me` to show sampling approval.
3. Invoke `unlock_grand_finale`. Visual Studio receives `notifications/tools/list_changed`, clears prior permissions as a rug-pull defense, and discovers `launch_grand_finale` without restarting the server.
4. Invoke `produce_show_package`. Visual Studio runs it as an ordinary tool; explain that adding the Tasks opt-in changes the same invocation into a task without changing the tool implementation.

## Reliability notes

- Everything is local and deterministic enough for conference Wi-Fi failure.
- The Fireworks audience page contains a tiny SignalR JSON-protocol client, so it has no CDN or npm dependency.
- Visual Studio's public documentation confirms prompts, resource templates, elicitation, sampling, live tool-list updates, trust, and tool approvals.
- MCP Apps rendering depends on the Visual Studio build. The synchronized browser is the complete visual fallback.
- Visual Studio Tasks-extension support is not assumed. The producer intentionally preserves normal inline execution.
- OAuth is already demonstrated by [ProtectedMcpServer](ProtectedMcpServer/README.md), so these demos need no credentials.
