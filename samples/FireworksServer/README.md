# Fireworks Server

A stage-ready MCP demo where one tool call launches the same animated show in an MCP App and every connected browser. The server combines MCP, structured content, MCP Apps, ASP.NET Core, and SignalR without a JavaScript package or cloud dependency.

## What it shows

- `launch_fireworks` returns a structured show plan linked to an MCP App with `[McpAppUi]`.
- The same object is broadcast to audience browsers through a SignalR hub.
- `choreograph_fireworks` is a prompt with arguments that guides the model into the tool.
- `show://fireworks/palettes/{theme}` is a resource template with an argument.
- The server can use stdio for Visual Studio while its browser dashboard runs over HTTP, or expose a stateless Streamable HTTP MCP endpoint.

## Visual Studio demo

Build once before Visual Studio starts the stdio server:

```powershell
dotnet build samples\FireworksServer\FireworksServer.csproj
```

The repository `.mcp.json` starts this sample in stdio mode. In Visual Studio:

1. Trust and start **Fireworks Show Control** in the MCP server manager.
2. Open <http://localhost:5399> in one or more audience browser windows.
3. Add the `choreograph_fireworks` prompt and set `occasion` to your event.
4. Let Copilot call `launch_fireworks`, approve the tool, and watch every connected browser launch together.

Visual Studio is not currently listed in the official MCP Apps client matrix, so the SignalR browser is the guaranteed stage output. The same structured result can render inline when the sample is connected to a host that supports MCP Apps.

## HTTP mode

```powershell
dotnet run --project samples\FireworksServer\FireworksServer.csproj
```

The MCP endpoint is `http://127.0.0.1:5399/mcp` and the audience display is `http://127.0.0.1:5399`.

Use a different port with `--ListenUrl=http://127.0.0.1:5400 --DashboardUrl=http://localhost:5400`. Select stdio explicitly with `--McpTransport=stdio`.

### Audience devices on the LAN

Loopback is the safe default. For a stage rehearsal with phones or other computers, change the Fireworks entry in `.mcp.json` to:

```json
"--ListenUrl=http://0.0.0.0:5399"
```

Restart the server, allow the port through the private-network firewall if prompted, and open `http://<presenter-ip>:5399` on each device. This exposes the read-only audience feed to the local network; restore the loopback value after the demo.
