using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace ColorPickerMcpApp.Resources;

/// <summary>
/// Provides the UI resource for the color picker MCP App.
/// The resource uses the ui:// scheme and text/html;profile=mcp-app MIME type
/// as specified by SEP-1865 (MCP Apps specification).
/// </summary>
[McpServerResourceType]
public class ColorPickerResources
{
    /// <summary>
    /// The color picker UI HTML resource.
    /// Returns an HTML page that implements the MCP Apps protocol via postMessage.
    /// </summary>
    [McpServerResource(
        UriTemplate = "ui://color-picker/picker",
        Name = "color_picker_ui",
        MimeType = "text/html;profile=mcp-app")]
    public static ResourceContents GetColorPickerUI() => new TextResourceContents
    {
        Uri = "ui://color-picker/picker",
        MimeType = "text/html;profile=mcp-app",
        Text = ColorPickerHtml,
        // Add UI metadata as per SEP-1865
        Meta = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["prefersBorder"] = true
            }
        }
    };

    /// <summary>
    /// The HTML content for the color picker MCP App.
    /// Implements the SEP-1865 protocol for communication with the host.
    /// </summary>
    private const string ColorPickerHtml = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Color Picker</title>
    <style>
        :root {
            /* Default theme variables - host can override via ui/initialize hostContext.styles */
            --color-background-primary: light-dark(#ffffff, #1a1a1a);
            --color-text-primary: light-dark(#1a1a1a, #ffffff);
            --color-text-secondary: light-dark(#6b7280, #9ca3af);
            --color-border-primary: light-dark(#e5e7eb, #374151);
            --color-background-info: light-dark(#eff6ff, #1e3a5f);
            --border-radius-md: 8px;
            --border-radius-lg: 12px;
            --font-sans: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
        }

        * {
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }

        html {
            color-scheme: light dark;
        }

        body {
            font-family: var(--font-sans);
            background: var(--color-background-primary);
            color: var(--color-text-primary);
            padding: 20px;
            min-height: 100vh;
        }

        .container {
            max-width: 400px;
            margin: 0 auto;
        }

        .header {
            text-align: center;
            margin-bottom: 24px;
        }

        .header h1 {
            font-size: 1.5rem;
            font-weight: 600;
            margin-bottom: 8px;
        }

        .header p {
            color: var(--color-text-secondary);
            font-size: 0.875rem;
        }

        .picker-section {
            background: var(--color-background-info);
            border: 1px solid var(--color-border-primary);
            border-radius: var(--border-radius-lg);
            padding: 24px;
            margin-bottom: 20px;
        }

        .color-input-wrapper {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 16px;
        }

        .color-input {
            width: 150px;
            height: 150px;
            border: 3px solid var(--color-border-primary);
            border-radius: var(--border-radius-md);
            cursor: pointer;
            background: none;
            padding: 0;
        }

        .color-input::-webkit-color-swatch-wrapper {
            padding: 0;
        }

        .color-input::-webkit-color-swatch {
            border: none;
            border-radius: calc(var(--border-radius-md) - 2px);
        }

        .color-value {
            font-family: monospace;
            font-size: 1.25rem;
            font-weight: 600;
            text-transform: uppercase;
        }

        .color-preview {
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 16px;
            background: var(--color-background-primary);
            border: 1px solid var(--color-border-primary);
            border-radius: var(--border-radius-md);
            width: 100%;
        }

        .preview-swatch {
            width: 32px;
            height: 32px;
            border-radius: 4px;
            border: 1px solid var(--color-border-primary);
        }

        .submit-btn {
            width: 100%;
            padding: 14px 24px;
            font-size: 1rem;
            font-weight: 600;
            color: white;
            background: #2563eb;
            border: none;
            border-radius: var(--border-radius-md);
            cursor: pointer;
            transition: background 0.2s;
        }

        .submit-btn:hover {
            background: #1d4ed8;
        }

        .submit-btn:active {
            background: #1e40af;
        }

        .submit-btn:disabled {
            background: #9ca3af;
            cursor: not-allowed;
        }

        .status {
            text-align: center;
            padding: 12px;
            font-size: 0.875rem;
            color: var(--color-text-secondary);
        }

        .status.loading {
            animation: pulse 1.5s infinite;
        }

        .status.success {
            color: #22c55e;
        }

        .status.error {
            color: #ef4444;
        }

        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.5; }
        }

        .prompt-text {
            text-align: center;
            font-size: 0.9rem;
            color: var(--color-text-secondary);
            margin-bottom: 16px;
            font-style: italic;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🎨 Color Picker</h1>
            <p>Select a color and submit your choice</p>
        </div>

        <div id="prompt" class="prompt-text" style="display: none;"></div>

        <div class="picker-section">
            <div class="color-input-wrapper">
                <input type="color" id="colorPicker" class="color-input" value="#3b82f6">
                <div class="color-preview">
                    <div id="previewSwatch" class="preview-swatch" style="background: #3b82f6;"></div>
                    <span id="colorValue" class="color-value">#3b82f6</span>
                </div>
            </div>
        </div>

        <button id="submitBtn" class="submit-btn" disabled>Submit Color</button>

        <div id="status" class="status loading">Initializing...</div>
    </div>

    <script>
        // MCP Apps communication via JSON-RPC over postMessage
        (function() {
            'use strict';

            let nextId = 1;
            const pendingRequests = new Map();
            let initialized = false;
            let hostProtocolVersion = null;

            // DOM elements
            const colorPicker = document.getElementById('colorPicker');
            const previewSwatch = document.getElementById('previewSwatch');
            const colorValue = document.getElementById('colorValue');
            const submitBtn = document.getElementById('submitBtn');
            const statusEl = document.getElementById('status');
            const promptEl = document.getElementById('prompt');

            // Update color preview when picker changes
            colorPicker.addEventListener('input', (e) => {
                const color = e.target.value;
                previewSwatch.style.background = color;
                colorValue.textContent = color;
            });

            // Submit color button handler
            submitBtn.addEventListener('click', async () => {
                const color = colorPicker.value;
                setStatus('Submitting color...', 'loading');
                submitBtn.disabled = true;

                try {
                    // Call the submit_color tool on the server
                    const result = await sendRequest('tools/call', {
                        name: 'submit_color',
                        arguments: {
                            color: color
                        }
                    });

                    setStatus(`Color ${color} submitted successfully!`, 'success');

                    // Send context update to keep the model informed
                    await sendRequest('ui/update-model-context', {
                        content: [{
                            type: 'text',
                            text: `User selected color: ${color}`
                        }]
                    });

                } catch (error) {
                    setStatus(`Error: ${error.message}`, 'error');
                    submitBtn.disabled = false;
                }
            });

            function setStatus(message, type = '') {
                statusEl.textContent = message;
                statusEl.className = 'status ' + type;
            }

            function applyHostStyles(hostContext) {
                // Apply theme
                if (hostContext.theme) {
                    document.documentElement.style.colorScheme = hostContext.theme;
                }

                // Apply CSS variables from host
                if (hostContext.styles?.variables) {
                    const root = document.documentElement;
                    for (const [key, value] of Object.entries(hostContext.styles.variables)) {
                        if (value) {
                            root.style.setProperty(key, value);
                        }
                    }
                }

                // Apply fonts if provided
                if (hostContext.styles?.css?.fonts) {
                    const style = document.createElement('style');
                    style.textContent = hostContext.styles.css.fonts;
                    document.head.appendChild(style);
                }
            }

            // Send a JSON-RPC request and wait for response
            function sendRequest(method, params) {
                return new Promise((resolve, reject) => {
                    const id = nextId++;
                    pendingRequests.set(id, { resolve, reject });

                    window.parent.postMessage({
                        jsonrpc: '2.0',
                        id: id,
                        method: method,
                        params: params
                    }, '*');
                });
            }

            // Send a JSON-RPC notification (no response expected)
            function sendNotification(method, params) {
                window.parent.postMessage({
                    jsonrpc: '2.0',
                    method: method,
                    params: params
                }, '*');
            }

            // Handle incoming messages from host
            window.addEventListener('message', (event) => {
                const data = event.data;
                if (!data || typeof data !== 'object' || data.jsonrpc !== '2.0') {
                    return;
                }

                // Handle response to our request
                if ('id' in data && data.id !== null) {
                    const pending = pendingRequests.get(data.id);
                    if (pending) {
                        pendingRequests.delete(data.id);
                        if (data.error) {
                            pending.reject(new Error(data.error.message || 'Unknown error'));
                        } else {
                            pending.resolve(data.result);
                        }
                    }
                    return;
                }

                // Handle notifications from host
                if (data.method) {
                    handleNotification(data.method, data.params);
                }
            });

            function handleNotification(method, params) {
                switch (method) {
                    case 'ui/notifications/tool-input':
                        // Handle initial tool input from host
                        if (params?.arguments) {
                            const args = params.arguments;

                            // Set initial color if provided
                            if (args.initialColor) {
                                colorPicker.value = args.initialColor;
                                previewSwatch.style.background = args.initialColor;
                                colorValue.textContent = args.initialColor;
                            }

                            // Set prompt if provided
                            if (args.prompt) {
                                promptEl.textContent = args.prompt;
                                promptEl.style.display = 'block';
                            }
                        }
                        break;

                    case 'ui/notifications/tool-result':
                        // Tool execution completed
                        setStatus('Ready', '');
                        submitBtn.disabled = false;
                        break;

                    case 'ui/notifications/tool-cancelled':
                        // Tool execution was cancelled
                        setStatus(`Cancelled: ${params?.reason || 'Unknown reason'}`, 'error');
                        break;

                    case 'ui/notifications/host-context-changed':
                        // Host context changed (e.g., theme toggle)
                        if (params) {
                            applyHostStyles(params);
                        }
                        break;

                    case 'ui/resource-teardown':
                        // Clean up before teardown
                        setStatus('Closing...', 'loading');
                        break;
                }
            }

            // Initialize MCP Apps connection
            async function initialize() {
                try {
                    const result = await sendRequest('ui/initialize', {
                        protocolVersion: '2025-06-18',
                        appInfo: {
                            name: 'ColorPickerMcpApp',
                            version: '1.0.0'
                        },
                        appCapabilities: {
                            availableDisplayModes: ['inline']
                        }
                    });

                    initialized = true;
                    hostProtocolVersion = result.protocolVersion;

                    // Apply host context styling
                    if (result.hostContext) {
                        applyHostStyles(result.hostContext);
                    }

                    // Send initialized notification
                    sendNotification('ui/notifications/initialized', {});

                    // Enable interaction
                    setStatus('Select a color and click Submit', '');
                    submitBtn.disabled = false;

                } catch (error) {
                    setStatus(`Initialization failed: ${error.message}`, 'error');
                }
            }

            // Start initialization when page loads
            initialize();
        })();
    </script>
</body>
</html>
""";
}
