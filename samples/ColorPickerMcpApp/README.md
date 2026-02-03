# Color Picker MCP App Sample

This sample demonstrates how to implement an **MCP App** (SEP-1865) using the MCP C# SDK. MCP Apps are interactive user interfaces that can be displayed within MCP hosts, enabling rich visual experiences beyond plain text.

## What is an MCP App?

MCP Apps (formerly MCP-UI) is an extension to MCP that enables servers to deliver interactive HTML-based user interfaces to hosts. This sample shows how to:

1. Declare **UI Resources** using the `ui://` URI scheme
2. Associate **Tools with UI resources** via `_meta.ui.resourceUri` metadata
3. Handle **`ui/initialize`** and other MCP Apps protocol messages using message filters
4. Create **interactive HTML** that communicates with the host via JSON-RPC over postMessage

## Architecture

```
┌─────────────────────┐     ┌─────────────────────────────────────────┐
│   MCP Host/Client   │────▶│        ColorPickerMcpApp Server         │
│  (supports MCP Apps)│     │                                         │
│                     │◀────│  ┌─────────────────────────────────┐   │
│  ┌───────────────┐  │     │  │  Tools                          │   │
│  │ HTML UI iframe │  │     │  │  - request_color_pick (_meta.ui)│   │
│  │ (Color Picker) │  │     │  │  - submit_color (app-only)      │   │
│  └───────────────┘  │     │  └─────────────────────────────────┘   │
│                     │     │  ┌─────────────────────────────────┐   │
│  ui/initialize ─────│─────│──│  Message Filters                │   │
│  tools/call ────────│─────│──│  - ui/initialize handler         │   │
│  resources/read ────│─────│──│  - ui/message handler            │   │
│                     │     │  │  - ui/notifications handlers     │   │
│                     │     │  └─────────────────────────────────┘   │
│                     │     │  ┌─────────────────────────────────┐   │
│                     │     │  │  Resources                       │   │
│                     │◀────│──│  - ui://color-picker/picker      │   │
│                     │     │  │    (text/html;profile=mcp-app)   │   │
│                     │     │  └─────────────────────────────────┘   │
└─────────────────────┘     └─────────────────────────────────────────┘
```

## Key Implementation Details

### 1. UI Resource Declaration

The color picker HTML is served as an MCP resource with the `ui://` scheme and `text/html;profile=mcp-app` MIME type:

```csharp
[McpServerResource(
    UriTemplate = "ui://color-picker/picker",
    Name = "color_picker_ui",
    MimeType = "text/html;profile=mcp-app")]
public static ResourceContents GetColorPickerUI() => new TextResourceContents
{
    Uri = "ui://color-picker/picker",
    MimeType = "text/html;profile=mcp-app",
    Text = ColorPickerHtml,
    Meta = new JsonObject
    {
        ["ui"] = new JsonObject { ["prefersBorder"] = true }
    }
};
```

### 2. Tool-UI Association

Tools are linked to UI resources via the `_meta.ui.resourceUri` metadata:

```csharp
[McpServerTool]
[Description("Request the user to pick a color using an interactive UI.")]
[McpMeta("ui", JsonValue = """{"resourceUri": "ui://color-picker/picker"}""")]
public static CallToolResult RequestColorPick(string? prompt = null, string? initialColor = null)
{
    // Tool implementation
}
```

### 3. Handling `ui/initialize` with Message Filters

The `AddIncomingMessageFilter` extension method intercepts the `ui/initialize` request and responds with host capabilities:

```csharp
builder.Services.AddMcpServer()
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request &&
            request.Method == "ui/initialize")
        {
            // Build and send McpUiInitializeResult response
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject
                {
                    ["protocolVersion"] = "2025-06-18",
                    ["hostCapabilities"] = new JsonObject { ... },
                    ["hostContext"] = new JsonObject { ... }
                }
            };
            await context.Server.SendMessageAsync(response, cancellationToken);
            return; // Don't call next - we handled it
        }
        await next(context, cancellationToken);
    });
```

### 4. HTML UI Communication

The HTML color picker communicates with the host via JSON-RPC over postMessage:

```javascript
// Send ui/initialize request
const result = await sendRequest('ui/initialize', {
    protocolVersion: '2025-06-18',
    clientInfo: { name: 'ColorPickerMcpApp', version: '1.0.0' },
    appCapabilities: { availableDisplayModes: ['inline'] }
});

// Call tools
await sendRequest('tools/call', {
    name: 'submit_color',
    arguments: { color: '#ff5733' }
});
```

## Running the Sample

1. **Build the sample:**
   ```bash
   dotnet build samples/ColorPickerMcpApp
   ```

2. **Run the server:**
   ```bash
   dotnet run --project samples/ColorPickerMcpApp
   ```

3. **Connect with an MCP client that supports MCP Apps** (clients must advertise the `io.modelcontextprotocol/ui` extension capability)

## SEP-1865 Compliance

This sample implements the following aspects of the SEP-1865 (MCP Apps) specification:

- ✅ UI Resource declaration with `ui://` scheme
- ✅ `text/html;profile=mcp-app` MIME type
- ✅ Tool-UI linkage via `_meta.ui.resourceUri`
- ✅ `ui/initialize` request/response handling
- ✅ `ui/notifications/initialized` notification
- ✅ Host context with theming (CSS variables)
- ✅ `tools/call` from UI to server
- ✅ `ui/update-model-context` for context updates
- ✅ `ui/open-link`, `ui/message` request handling

## Notes

- This sample uses **low-level message filters** to handle MCP Apps protocol messages since the SDK doesn't have built-in high-level support for MCP Apps yet
- The HTML UI is embedded as a C# raw string literal for simplicity
- A real MCP host would render the HTML in a sandboxed iframe and proxy the postMessage communication
