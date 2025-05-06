using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpSseTests(ITestOutputHelper outputHelper) : MapMcpTests(outputHelper)
{
    protected override bool UseStreamableHttp => false;

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

        var mcpClient = await ConnectAsync(requestPath);

        Assert.Equal("TestCustomRouteServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task Can_UseHttpContextAccessor_InTool()
    {
        Builder.Services.AddMcpServer().WithHttpTransport().WithTools<EchoHttpContextUserTools>();

        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                context.User = CreateUser("TestUser");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var mcpClient = await ConnectAsync();

        var response = await mcpClient.CallToolAsync(
            "EchoWithUserName",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content);
        Assert.Equal("TestUser: Hello world!", content.Text);
    }
}
