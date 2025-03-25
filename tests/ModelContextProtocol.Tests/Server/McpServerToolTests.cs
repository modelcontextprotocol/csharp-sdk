using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

public class McpServerToolTests
{
    [Fact]
    public void Create_InvalidArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>("function", () => McpServerTool.Create(null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((MethodInfo)null!));
        Assert.Throws<ArgumentNullException>("method", () => McpServerTool.Create((Delegate)null!));
    }

    [Fact]
    public async Task SupportsIMcpServer()
    {
        Mock<IMcpServer> mockServer = new();

        McpServerTool tool = McpServerTool.Create((IMcpServer server) =>
        {
            Assert.Same(mockServer.Object, server);
            return "42";
        });

        Assert.DoesNotContain("server", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create((MyService actualMyService) =>
        {
            Assert.Same(expectedMyService, actualMyService);
            return "42";
        }, services: services);

        Assert.DoesNotContain("actualMyService", JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        Mock<IMcpServer> mockServer = new();

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsError);

        mockServer.SetupGet(x => x.Services).Returns(services);

        result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(mockServer.Object, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    [Fact]
    public async Task SupportsOptionalServiceFromDI()
    {
        MyService expectedMyService = new();

        ServiceCollection sc = new();
        sc.AddSingleton(expectedMyService);
        IServiceProvider services = sc.BuildServiceProvider();

        McpServerTool tool = McpServerTool.Create((MyService? actualMyService = null) =>
        {
            Assert.Null(actualMyService);
            return "42";
        }, services: services);

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(null!, null),
            TestContext.Current.CancellationToken);
        Assert.Equal("42", result.Content[0].Text);
    }

    private sealed class MyService;
}
