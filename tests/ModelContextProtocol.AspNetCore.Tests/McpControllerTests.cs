using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.AspNetCore.Tests;

public class McpControllerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    private async Task<McpClient> ConnectAsync()
    {
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanConnect_WithMcpClient_ViaController()
    {
        Builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestMcpController).Assembly);
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "ControllerTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport().WithTools<ControllerTestTools>();

        await using var app = Builder.Build();

        app.MapControllers();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        Assert.Equal("ControllerTestServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task CanCallTool_ViaController()
    {
        Builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestMcpController).Assembly);
        Builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<ControllerTestTools>();

        await using var app = Builder.Build();

        app.MapControllers();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        var result = await mcpClient.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "Hello from controller!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var textContent = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("hello Hello from controller!", textContent.Text);
    }

    [Fact]
    public async Task CanListTools_ViaController()
    {
        Builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestMcpController).Assembly);
        Builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<ControllerTestTools>();

        await using var app = Builder.Build();

        app.MapControllers();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "echo");
    }
}

[McpServerToolType]
internal sealed class ControllerTestTools
{
    [McpServerTool(Name = "echo"), Description("Echoes the input back to the client.")]
    public static string Echo(string message) => "hello " + message;
}

[ApiController]
[Route("mcp")]
public class TestMcpController : ControllerBase
{
    private static readonly RequestDelegate _mcpHandler = McpRequestDelegateFactory.Create();

    [HttpPost]
    [HttpGet]
    [HttpDelete]
    public Task Handle() => _mcpHandler(HttpContext);
}
