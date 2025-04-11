using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private async Task<IMcpClient> ConnectAsync(string? path = null)
    {
        var sseClientTransportOptions = new SseClientTransportOptions()
        {
            Endpoint = new Uri($"http://localhost{path}"),
        };
        await using var transport = new SseClientTransport(sseClientTransportOptions, HttpClient, LoggerFactory);
        return await McpClientFactory.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MapMcp_ThrowsInvalidOperationException_IfWithHttpTransportIsNotCalled()
    {
        Builder.Services.AddMcpServer();
        await using var app = Builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMcp());
        Assert.StartsWith("You must call WithHttpTransport()", exception.Message);
    }

    [Fact]
    public async Task Allows_Customizing_Route()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();

        app.MapMcp("/mcp");

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await HttpClient.GetAsync("http://localhost/mcp/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("/a", "/a/sse")]
    [InlineData("/a", "/a/")]
    [InlineData("/a/", "/a/sse")]
    [InlineData("/a/", "/a/")]
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
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "TestUser")], "TestAuthType", "name", "role"));
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

    [McpServerToolType]
    private class EchoHttpContextUserTools(IHttpContextAccessor contextAccessor)
    {
        [McpServerTool, Description("Echoes the input back to the client with their user name.")]
        public string EchoWithUserName(string message)
        {
            var httpContext = contextAccessor.HttpContext ?? throw new Exception("HttpContext unavailable!");
            var userName = httpContext.User.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }
}
