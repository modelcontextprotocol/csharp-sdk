using ColorPickerMcpApp.Resources;
using ColorPickerMcpApp.Tools;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

// Configure the MCP server with the color picker app
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<ColorPickerTools>()
    .WithResources<ColorPickerResources>()
    // Handle ui/initialize requests from MCP Apps (SEP-1865)
    // This filter intercepts the ui/initialize request and responds with McpUiInitializeResult
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "ui/initialize")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            logger?.LogInformation("Handling ui/initialize request from MCP App");

            // Parse the incoming request params
            var paramsJson = request.Params?.ToJsonString() ?? "{}";
            logger?.LogDebug("ui/initialize params: {Params}", paramsJson);

            // Build the McpUiInitializeResult response as per SEP-1865
            var result = new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["hostCapabilities"] = new JsonObject
                {
                    // Indicate what this host/server supports
                    ["serverTools"] = new JsonObject
                    {
                        ["listChanged"] = true
                    },
                    ["serverResources"] = new JsonObject
                    {
                        ["listChanged"] = true
                    },
                    ["logging"] = new JsonObject()
                },
                ["hostInfo"] = new JsonObject
                {
                    ["name"] = "ColorPickerMcpApp",
                    ["version"] = "1.0.0"
                },
                ["hostContext"] = new JsonObject
                {
                    // Provide theme and styling info to the UI
                    ["theme"] = "light",
                    ["displayMode"] = "inline",
                    ["availableDisplayModes"] = new JsonArray("inline"),
                    ["containerDimensions"] = new JsonObject
                    {
                        ["maxWidth"] = 450,
                        ["maxHeight"] = 500
                    },
                    ["styles"] = new JsonObject
                    {
                        ["variables"] = new JsonObject
                        {
                            // Provide some sample theme variables
                            ["--color-background-primary"] = "light-dark(#ffffff, #1a1a1a)",
                            ["--color-text-primary"] = "light-dark(#1a1a1a, #ffffff)",
                            ["--color-border-primary"] = "light-dark(#e5e7eb, #374151)",
                            ["--border-radius-md"] = "8px",
                            ["--font-sans"] = "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"
                        }
                    },
                    ["locale"] = "en-US",
                    ["platform"] = "web"
                }
            };

            // Send the response
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };

            await context.Server.SendMessageAsync(response, cancellationToken);

            logger?.LogInformation("Sent ui/initialize response to MCP App");

            // Don't call next - we've handled this request
            return;
        }

        // Pass through to normal handlers for all other requests
        await next(context, cancellationToken);
    })
    // Handle ui/open-link requests (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "ui/open-link")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            var url = request.Params?["url"]?.GetValue<string>();

            logger?.LogInformation("MCP App requested to open link: {Url}", url);

            // For this sample, we just acknowledge the request
            // In a real host, this would open the URL in the user's browser
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject()
            };

            await context.Server.SendMessageAsync(response, cancellationToken);
            return;
        }

        await next(context, cancellationToken);
    })
    // Handle ui/message requests (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "ui/message")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            var role = request.Params?["role"]?.GetValue<string>();
            var content = request.Params?["content"]?.ToJsonString();

            logger?.LogInformation("MCP App sent message with role {Role}: {Content}", role, content);

            // Acknowledge the message
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject()
            };

            await context.Server.SendMessageAsync(response, cancellationToken);
            return;
        }

        await next(context, cancellationToken);
    })
    // Handle ui/update-model-context requests (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "ui/update-model-context")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            var content = request.Params?["content"]?.ToJsonString();
            var structuredContent = request.Params?["structuredContent"]?.ToJsonString();

            logger?.LogInformation("MCP App updated model context: content={Content}, structured={Structured}",
                content, structuredContent);

            // Acknowledge the update
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject()
            };

            await context.Server.SendMessageAsync(response, cancellationToken);
            return;
        }

        await next(context, cancellationToken);
    })
    // Handle ui/request-display-mode requests (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == "ui/request-display-mode")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            var requestedMode = request.Params?["mode"]?.GetValue<string>();

            logger?.LogInformation("MCP App requested display mode: {Mode}", requestedMode);

            // For this sample, we only support inline mode
            var response = new JsonRpcResponse
            {
                Id = request.Id,
                Result = new JsonObject
                {
                    ["mode"] = "inline"
                }
            };

            await context.Server.SendMessageAsync(response, cancellationToken);
            return;
        }

        await next(context, cancellationToken);
    })
    // Handle ui/notifications/initialized notification (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcNotification notification &&
            notification.Method == "ui/notifications/initialized")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            logger?.LogInformation("MCP App completed initialization");

            // No response needed for notifications
            return;
        }

        await next(context, cancellationToken);
    })
    // Handle ui/notifications/size-changed notification (SEP-1865)
    .AddIncomingMessageFilter(next => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcNotification notification &&
            notification.Method == "ui/notifications/size-changed")
        {
            var logger = context.Services?.GetService<ILogger<Program>>();
            var width = notification.Params?["width"]?.GetValue<int>();
            var height = notification.Params?["height"]?.GetValue<int>();

            logger?.LogDebug("MCP App size changed: {Width}x{Height}", width, height);

            // No response needed for notifications
            return;
        }

        await next(context, cancellationToken);
    });

var app = builder.Build();

// Map MCP endpoints
app.MapMcp();

Console.WriteLine("Color Picker MCP App Server started!");
Console.WriteLine("This server demonstrates the SEP-1865 MCP Apps specification.");
Console.WriteLine("Connect using an MCP client that supports the io.modelcontextprotocol/ui extension.");

app.Run();
