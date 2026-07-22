# Visual Studio MCP Demos

The two core stage demos show how little C# is needed to connect Visual Studio to live systems and interactive workflows. A third Tasks sample remains available as an appendix. Visual Studio is the client; there are no sample client projects.

| Demo | Stage moment | MCP features |
| --- | --- | --- |
| [Fireworks Server](FireworksServer/README.md) | One tool call launches synchronized fireworks in every connected browser; supported hosts can render the same structured result as an MCP App. | Tools, structured content, MCP Apps metadata, prompts, resource templates, server instructions, stdio, stateless HTTP |
| [Stateless Release Planner](ReleasePlannerServer/README.md) | Visual Studio shows familiar forms while the handler uses client-carried MRTR state; a simulated deployment tool appears live. | MRTR, elicitation compatibility, `tools/list_changed`, dynamic tools |
| [Task-Aware Show Producer](ShowProducerServer/README.md) | Appendix sample: the same tool runs inline or becomes pollable, interactive, and cancellable for a Tasks-aware host. | Tasks extension, input-required tasks, polling, cancellation, inline fallback |

## One-time setup

Use Visual Studio 2026 18.7 or later, open `ModelContextProtocol.slnx`, and build the solution in the default **Debug** configuration:

```powershell
dotnet build
```

The checked-in `.mcp.json` points Visual Studio at the two core demo assemblies. In the MCP server manager, trust and start those servers. Rebuild after changing a sample, then save `.mcp.json` to make Visual Studio restart the processes.

## Suggested eleven-minute stage flow

1. Open <http://localhost:5399> in one or more browser windows. For physical audience devices, enable the opt-in LAN binding documented in the Fireworks README. Add the **choreograph_fireworks** prompt in Visual Studio, enter an occasion, and approve `launch_fireworks`. The tool returns the same structured show payload used by supported MCP Apps hosts and all connected browsers.
2. Ask: `Use plan_release to prepare version 2.0.0 of Contoso.Api for production.` Choose a canary rollout at 10 percent traffic with rollback above 1 percent errors, approve the second form, and point out the final "stateless proof" line.
3. Invoke `unlock_production_deploy`. Visual Studio receives `notifications/tools/list_changed`, clears prior permissions as a rug-pull defense, and discovers the simulated `deploy_release` tool without restarting the server.

## Reliability notes

- Everything is local and deterministic enough for conference Wi-Fi failure.
- The Fireworks audience page contains a tiny SignalR JSON-protocol client, so it has no CDN or npm dependency.
- Visual Studio's public documentation confirms prompts, resource templates, elicitation, sampling, live tool-list updates, trust, and tool approvals.
- Visual Studio is not currently listed in the official MCP Apps client matrix. The synchronized browser is the guaranteed visual path.
- Visual Studio Tasks-extension support is not assumed, so the producer stays in the appendix.
- OAuth is covered by [ProtectedMcpServer](ProtectedMcpServer/README.md). Its test issuer is suitable only for an optional, fully rehearsed local demo.
