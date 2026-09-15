using AspNetCoreMcpClient;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var endpoint = new Uri(builder.Configuration["McpServer:Endpoint"] ?? "http://localhost:3001");
var idleTimeout = TimeSpan.FromMinutes(builder.Configuration.GetValue("McpServer:IdleTimeoutMinutes", 20));

builder.Services.AddHttpClient("mcp-server");
builder.Services.AddSingleton<ElicitationBroker>();
builder.Services.AddSingleton(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
    var elicitationBroker = serviceProvider.GetRequiredService<ElicitationBroker>();

    return new SessionClientRegistry<McpClientConnection>(
        async (sessionId, cancellationToken) =>
        {
            var httpClient = httpClientFactory.CreateClient("mcp-server");
            var transport = new HttpClientTransport(
                new()
                {
                    Endpoint = endpoint,
                    Name = $"ASP.NET Core session {sessionId}",
                    TransportMode = HttpTransportMode.StreamableHttp,
                },
                httpClient,
                loggerFactory,
                ownsHttpClient: true);

            try
            {
                var client = await McpClient.CreateAsync(
                    transport,
                    new()
                    {
                        ClientInfo = new() { Name = "AspNetCoreMcpClient", Version = "1.0.0" },
                        Handlers = new()
                        {
                            ElicitationHandler = (request, token) =>
                                elicitationBroker.RequestAsync(sessionId, request, token),
                        },
                    },
                    loggerFactory,
                    cancellationToken);

                return new McpClientConnection(client, transport);
            }
            catch
            {
                await transport.DisposeAsync();
                throw;
            }
        },
        TimeProvider.System,
        idleTimeout);
});
builder.Services.AddHostedService<McpClientCleanupService>();

var app = builder.Build();

// Disposing the registry waits for each session's in-flight operation to finish. A tool call blocked on an unanswered
// elicitation would never finish, so release those requests before shutdown disposes the singleton registry.
app.Lifetime.ApplicationStopping.Register(() =>
{
    var broker = app.Services.GetRequiredService<ElicitationBroker>();
    var canceled = broker.CancelAll();
    if (canceled > 0)
    {
        app.Logger.LogInformation("Canceled {Count} pending elicitations during shutdown.", canceled);
    }
});

app.MapGet("/tools", async (
    HttpContext context,
    SessionClientRegistry<McpClientConnection> registry,
    CancellationToken cancellationToken) =>
{
    var sessionId = GetDemoSessionId(context);
    var tools = await registry.ExecuteAsync(
        sessionId,
        async (connection, token) => await connection.Client.ListToolsAsync(cancellationToken: token),
        cancellationToken);

    return tools.Select(tool => new { tool.Name, tool.Description });
});

app.MapPost("/tools/{toolName}", async (
    string toolName,
    JsonElement? arguments,
    HttpContext context,
    SessionClientRegistry<McpClientConnection> registry,
    CancellationToken cancellationToken) =>
{
    var sessionId = GetDemoSessionId(context);
    var toolArguments = arguments is { ValueKind: JsonValueKind.Object }
        ? arguments.Value.Deserialize<Dictionary<string, object?>>()
        : null;

    // Progress notifications arrive on the MCP session's message loop, so use a thread-safe collection.
    var progressUpdates = new ConcurrentQueue<ProgressNotificationValue>();
    var progress = new InlineProgress<ProgressNotificationValue>(progressUpdates.Enqueue);
    var result = await registry.ExecuteAsync(
        sessionId,
        async (connection, token) => await connection.Client.CallToolAsync(
            toolName,
            toolArguments,
            progress,
            cancellationToken: token),
        cancellationToken);

    return Results.Ok(new { Result = result, Progress = progressUpdates });
});

app.MapGet("/elicitation", (HttpContext context, ElicitationBroker broker) =>
{
    var pending = broker.GetPending(GetDemoSessionId(context));
    return pending is null ? Results.NoContent() : Results.Ok(pending);
});

app.MapPost("/elicitation/{requestId:guid}", (
    Guid requestId,
    ElicitResult response,
    HttpContext context,
    ElicitationBroker broker) =>
{
    return broker.TryRespond(GetDemoSessionId(context), requestId, response)
        ? Results.Accepted()
        : Results.NotFound();
});

app.MapDelete("/session", async (
    HttpContext context,
    SessionClientRegistry<McpClientConnection> registry,
    ElicitationBroker broker) =>
{
    var sessionId = GetDemoSessionId(context);
    broker.Cancel(sessionId);
    return await registry.RemoveAsync(sessionId) ? Results.NoContent() : Results.NotFound();
});

app.Run();

static string GetDemoSessionId(HttpContext context)
{
    const string HeaderName = "X-Demo-User";
    var sessionId = context.Request.Headers[HeaderName].ToString();
    if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 128)
    {
        throw new BadHttpRequestException($"Provide a non-empty {HeaderName} header of at most 128 characters.");
    }

    // A production application should derive this key from authenticated server-side identity/session state.
    return sessionId;
}
