using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ResumabilityDemo.Server;

var builder = WebApplication.CreateBuilder(args);

// Check if event stream store should be disabled (for comparison testing)
var disableStore = args.Contains("--no-store") || args.Contains("--disable-store");

// Configure the distributed cache based on appsettings.json
builder.Services.AddConfiguredDistributedCache(builder.Configuration);

// Get cache provider for logging
var cacheProvider = builder.Configuration.GetValue<string>("CacheProvider") ?? "Memory";

// Register post-configure to set up the event stream store with the resolved IDistributedCache
// Skip this if --no-store is specified to test behavior without resumability
if (!disableStore)
{
    builder.Services.AddSingleton<IPostConfigureOptions<HttpServerTransportOptions>>(sp =>
        new EventStreamStorePostConfigure(sp, builder.Configuration));
}

// Register the transport registry for tracking active POST transports
builder.Services.AddSingleton<TransportRegistry>();

// Configure MCP server with HTTP transport and event stream store for resumability
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<ResumabilityTools>()
    .WithTools<TransportControlTools>();

var app = builder.Build();

// Log startup information
app.Logger.LogInformation("Starting ResumabilityDemo server with {CacheProvider} cache provider", cacheProvider);
app.Logger.LogInformation("Event stream store: {StoreStatus}", disableStore ? "DISABLED" : "Enabled");
app.Logger.LogInformation("MCP endpoint: POST/GET /mcp");

// Map the MCP endpoint
app.MapMcp("/mcp");

// Add a simple health check / info endpoint
app.MapGet("/", () => Results.Ok(new
{
    Name = "ResumabilityDemo MCP Server",
    CacheProvider = cacheProvider,
    EventStreamStore = disableStore ? "Disabled (--no-store)" : "Enabled",
    Endpoints = new
    {
        Mcp = "/mcp",
        Health = "/"
    },
    Tools = new[]
    {
        "Echo - Basic connectivity test",
        "DelayedEcho - Test resumability during long operations",
        "ProgressDemo - Test resuming mid-stream",
        "TriggerPollingMode - Test server-side disconnect and polling",
        "ProgressThenPolling - Full resumability + polling flow",
        "GenerateUniqueId - Verify same response on reconnection"
    }
}));

app.Run();
