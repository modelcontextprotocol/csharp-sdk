using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpSseTests(ITestOutputHelper outputHelper) : MapMcpTests(outputHelper)
{
    protected override bool UseStreamableHttp => false;
    protected override bool Stateless => false;

    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/secondary")]
    public async Task Allows_Customizing_Route(string pattern)
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp(pattern);

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await HttpClient.GetAsync($"http://localhost:5000{pattern}/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var sseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var sseStreamReader = new StreamReader(sseStream, System.Text.Encoding.UTF8);
        var eventLine = await sseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        var dataLine = await sseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(eventLine);
        Assert.Equal("event: endpoint", eventLine);
        Assert.NotNull(dataLine);
        Assert.Equal($"data: {pattern}/message", dataLine[..dataLine.IndexOf('?')]);
    }

    [Theory]
    [InlineData("/a", "/a/sse")]
    [InlineData("/a/", "/a/sse")]
    [InlineData("/a/b", "/a/b/sse")]
    public async Task CanConnect_WithMcpClient_AfterCustomizingRoute(string routePattern, string requestPath)
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

        app.MapMcp(routePattern);

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(requestPath);

        Assert.Equal("TestCustomRouteServer", mcpClient.ServerInfo.Name);
    }
}
