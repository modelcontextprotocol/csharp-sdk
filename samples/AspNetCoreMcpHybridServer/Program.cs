using AspNetCoreMcpHybridServer;
using AspNetCoreMcpHybridServer.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.Diagnostics;

const string McpEndpoint = "/mcp";
const string ModernProtocolVersion = "2026-07-28";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GreetingService>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "hybrid-http-sample",
            Version = "1.0.0",
        };
    })
    .WithHttpTransport(options => options.Stateless = false)
    .WithTools<InteractiveTools>();

var app = builder.Build();

await using var statelessServices = CreateStatelessServiceProvider(app);
var statelessApp = new ApplicationBuilder(statelessServices);
statelessApp.UseRouting();
statelessApp.UseEndpoints(endpoints => endpoints.MapMcp(McpEndpoint));
var statelessMcp = statelessApp.Build();

// This must run before the main application's routing so the stateful endpoint has not
// already been selected when the stateless pipeline performs its own endpoint routing.
app.Use(async (context, next) =>
{
    if (IsModernMcpRequest(context))
    {
        await statelessMcp(context);
        return;
    }

    await next(context);
});

app.UseRouting();
app.MapMcp(McpEndpoint);

await app.RunAsync();

static bool IsModernMcpRequest(HttpContext context)
{
    return HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path == McpEndpoint &&
        context.Request.Headers.TryGetValue("MCP-Protocol-Version", out var protocolVersion) &&
        StringComparer.Ordinal.Compare(protocolVersion.ToString(), ModernProtocolVersion) >= 0;
}

static ServiceProvider CreateStatelessServiceProvider(WebApplication app)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(app.Services.GetRequiredService<ILoggerFactory>());
    services.AddSingleton(app.Services.GetRequiredService<IHostApplicationLifetime>());
    services.AddSingleton(app.Services.GetRequiredService<DiagnosticListener>());
    services.AddRoutingCore();

    services
        .AddMcpServer()
        .WithHttpTransport(options => options.Stateless = true);

    // Reuse the main application's MCP configuration pipeline so both transports expose
    // the same tools and handlers. The stateless transport still resolves user services,
    // such as GreetingService, from the original HttpContext.RequestServices.
    services.AddSingleton(app.Services.GetRequiredService<IOptions<McpServerOptions>>());
    services.AddSingleton(app.Services.GetRequiredService<IOptionsFactory<McpServerOptions>>());

#pragma warning disable ASP0000 // A separate provider is required to isolate HttpServerTransportOptions.
    return services.BuildServiceProvider();
#pragma warning restore ASP0000
}
