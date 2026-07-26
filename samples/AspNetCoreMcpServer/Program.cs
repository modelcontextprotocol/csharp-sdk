using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using AspNetCoreMcpServer.Tools;
using AspNetCoreMcpServer.Resources;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];

// Only enable CORS if you intentionally want browser-based cross-origin access to this server.
// Keep the allowlist narrowly scoped to known origins. Broad CORS settings weaken security.
builder.Services.AddCors(options =>
{
    options.AddPolicy("McpBrowserClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "DELETE")
            // Browsers can send Accept without extra CORS configuration. These are the MCP-specific
            // and non-safelisted headers the browser-based client needs for stateful Streamable HTTP.
            .WithHeaders("Content-Type", "MCP-Protocol-Version", "Mcp-Session-Id")
            .WithExposedHeaders("Mcp-Session-Id");
    });
});

// Note: This sample uses SampleLlmTool which calls server.AsSamplingChatClient() to send
// a server-to-client sampling request. This requires stateful (session-based) mode. Set
// Stateless = false explicitly for forward compatibility in case the default changes.
// See https://csharp.sdk.modelcontextprotocol.io/concepts/sessions/sessions.html for details.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = false)
    .WithTools<EchoTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<WeatherTools>()
    .WithResources<SimpleResourceType>();

builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(b => b.AddMeter("*")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithLogging()
    .UseOtlpExporter();

// Configure HttpClientFactory for weather.gov API
builder.Services.AddHttpClient("WeatherApi", client =>
{
    client.BaseAddress = new Uri("https://api.weather.gov");
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
});

var app = builder.Build();

app.UseCors();
app.MapMcp().RequireCors("McpBrowserClient");

app.Run();
