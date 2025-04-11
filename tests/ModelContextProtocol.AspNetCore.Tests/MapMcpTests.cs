using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public async Task Allows_Customizing_Route()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp("/mcp");

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        using var response = await httpClient.GetAsync("http://localhost/mcp/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CanConnect_WithMcpClient_AfterCustomizingRoute()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestCustomRouteServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp("/mcp");

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        var sseClientTransportOptions = new SseClientTransportOptions()
        {
            Endpoint = new Uri("http://localhost/mcp/sse"),
        };
        await using var transport = new SseClientTransport(sseClientTransportOptions, httpClient, LoggerFactory);
        var mcpClient = await McpClientFactory.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("TestCustomRouteServer", mcpClient.ServerInfo.Name);
    }
}
