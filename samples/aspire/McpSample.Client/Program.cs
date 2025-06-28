using McpSample.Client.Components;
using ModelContextProtocol.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient("mcp-server", client => client.BaseAddress = new("https+http://mcp-server"));

builder.Services.AddSingleton(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    HttpClient httpClient = httpClientFactory.CreateClient("mcp-server");

    var endpoint = httpClient.BaseAddress?.ToString().Replace("https+", "") ?? throw new InvalidOperationException("No endpoint set for MCP server");

    var transport = new SseClientTransport(new SseClientTransportOptions(), httpClient);

    var t = McpClientFactory.CreateAsync(transport);
    t.Wait();
    return t.Result;
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
