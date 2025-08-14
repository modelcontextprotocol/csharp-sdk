using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpRoutingTests(ITestOutputHelper outputHelper) : MapMcpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => false;

    [Fact]
    public async Task WithHttpTransportAndRouting_ThrowsInvalidOperationException_IfWithHttpTransportIsNotCalled()
    {
        Builder.Services.AddMcpServer();
        await using var app = Builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMcpWithRouting());
        Assert.Contains("WithHttpTransport", exception.Message);
    }

    [Theory]
    [InlineData("/mcp", 4)] // Global route - all tools (global + admin + weather + multi_route)
    [InlineData("/mcp/admin", 2)] // Admin route - admin tools + global tools  
    [InlineData("/mcp/weather", 3)] // Weather route - weather + multi_route + global tools
    [InlineData("/mcp/math", 2)] // Math route - multi_route + global tools
    public async Task Route_Filtering_ReturnsCorrectToolCount(string route, int expectedToolCount)
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(route);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expectedToolCount, tools.Count);
    }

    [Fact]
    public async Task GlobalRoute_ReturnsAllTools()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/mcp");
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var toolNames = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("global_tool", toolNames);
        Assert.Contains("admin_tool", toolNames);
        Assert.Contains("weather_tool", toolNames);
    }

    [Fact]
    public async Task AdminRoute_OnlyReturnsAdminAndGlobalTools()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/mcp/admin");
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var toolNames = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("global_tool", toolNames);
        Assert.Contains("admin_tool", toolNames);
        Assert.DoesNotContain("weather_tool", toolNames);
    }

    [Fact]
    public async Task WeatherRoute_OnlyReturnsWeatherAndGlobalTools()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/mcp/weather");
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var toolNames = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("global_tool", toolNames);
        Assert.Contains("weather_tool", toolNames);
        Assert.DoesNotContain("admin_tool", toolNames);
    }

    [Theory]
    [InlineData("/mcp/")]
    [InlineData("/mcp")]
    public async Task TrailingSlash_HandledCorrectly_ForGlobalRoute(string route)
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(route);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Should return all tools (global route behavior)
        Assert.Equal(4, tools.Count);
    }

    [Theory]
    [InlineData("/mcp/admin")]
    [InlineData("/mcp/admin/")]
    public async Task TrailingSlash_HandledCorrectly_ForSpecificRoute(string route)
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(route);
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Should return admin + global tools only
        Assert.Equal(2, tools.Count);
        var toolNames = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("admin_tool", toolNames);
        Assert.Contains("global_tool", toolNames);
    }

    [Fact]
    public async Task MultiRouteTools_AppearOnAllSpecifiedRoutes()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Check math route
        await using var mathClient = await ConnectAsync("/mcp/math");
        var mathTools = await mathClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var mathToolNames = mathTools.Select(t => t.Name).ToHashSet();
        Assert.Contains("multi_route_tool", mathToolNames);

        // Check weather route  
        await using var weatherClient = await ConnectAsync("/mcp/weather");
        var weatherTools = await weatherClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var weatherToolNames = weatherTools.Select(t => t.Name).ToHashSet();
        Assert.Contains("multi_route_tool", weatherToolNames);
    }

    [Fact]
    public async Task ToolInvocation_WorksOnCorrectRoute()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransportAndRouting()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcpWithRouting("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/mcp/admin");
        var result = await mcpClient.CallToolAsync(
            "admin_tool",
            new Dictionary<string, object?>(),
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Admin tool executed", content.Text);
    }

    [Fact]
    public async Task BackwardCompatibility_StandardMapMcp_StillWorks()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<RoutingTestTools>();

        await using var app = Builder.Build();
        app.MapMcp("mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/mcp");
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Should return all tools when not using routing
        Assert.Equal(4, tools.Count); // All tools including multi-route
    }
}

[McpServerToolType]
public sealed class RoutingTestTools
{
    [McpServerTool(Name = "global_tool"), Description("Global tool available on all routes")]
    public static string GlobalTool()
    {
        return "Global tool executed";
    }

    [McpServerTool(Name = "admin_tool"), Description("Admin-only tool")]
    [McpServerToolRoute("admin")]
    public static string AdminTool()
    {
        return "Admin tool executed";
    }

    [McpServerTool(Name = "weather_tool"), Description("Weather-specific tool")]
    [McpServerToolRoute("weather")]
    public static string WeatherTool()
    {
        return "Weather tool executed";
    }

    [McpServerTool(Name = "multi_route_tool"), Description("Tool available on multiple routes")]
    [McpServerToolRoute("math", "weather")]
    public static string MultiRouteTool()
    {
        return "Multi-route tool executed";
    }
}