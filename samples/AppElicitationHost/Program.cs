using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps.Elicitation;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
var pendingStore = new PendingElicitationStore();

McpClient? mcpClient = null;
var capabilities = McpAppElicitation.AddClientCapabilities(new ClientCapabilities());
var clientOptions = new McpClientOptions
{
    ProtocolVersion = "2026-07-28",
    ClientInfo = new Implementation { Name = "minimal-app-elicitation-host", Version = "0.1.0" },
    Capabilities = capabilities,
};

clientOptions.Handlers.ElicitationHandler = async (request, cancellationToken) =>
{
    if (request is null || McpAppElicitation.GetAppUi(request) is not { } appUi)
    {
        return new ElicitResult { Action = "decline" };
    }

    var resource = await mcpClient!.ReadResourceAsync(appUi.ResourceUri, cancellationToken: cancellationToken);
    var html = resource.Contents.OfType<TextResourceContents>().Single().Text;
    return await pendingStore.PublishAsync(request, appUi.ResourceUri, html, cancellationToken);
};

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri("http://localhost:5100/mcp"),
    TransportMode = HttpTransportMode.StreamableHttp,
});
mcpClient = await McpClient.CreateAsync(transport, clientOptions);

var app = builder.Build();

app.MapGet("/", () => Results.File(
    Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
    "text/html"));

app.MapPost("/api/run", async (CancellationToken cancellationToken) =>
{
    var result = await mcpClient.CallToolAsync(
        "assign_account_manager",
        cancellationToken: cancellationToken);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No text result.";
    return Results.Ok(new { text, result.StructuredContent });
});

app.MapGet("/api/elicitation", () =>
{
    var pending = pendingStore.GetPending();
    return pending is null
        ? Results.NoContent()
        : Results.Ok(new
        {
            pending.Id,
            pending.ResourceUri,
            request = pending.Request,
            pending.Html,
        });
});

app.MapPost("/api/elicitation/{id}", (string id, SubmitElicitation submission) =>
{
    var completed = pendingStore.Complete(id, new ElicitResult
    {
        Action = submission.Action,
        Content = submission.Content,
    });
    return completed ? Results.Accepted() : Results.NotFound();
});

app.Lifetime.ApplicationStopping.Register(() => mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult());
app.Run("http://localhost:5200");
