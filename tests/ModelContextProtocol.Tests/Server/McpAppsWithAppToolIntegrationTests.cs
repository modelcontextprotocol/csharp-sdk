#pragma warning disable MCPEXP003

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that <see cref="McpAppsBuilderExtensions.WithAppTool"/> preserves the
/// tool and resource behavior across a client/server round trip.
/// </summary>
public sealed class McpAppsWithAppToolIntegrationTests : ClientServerTestBase
{
    public McpAppsWithAppToolIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithAppTool(
            AppTools.GetWeather,
            "ui://weather/view.html",
            static () => "<html><body>weather</body></html>");
    }

    [Fact]
    public async Task WithAppTool_RoundTripsToolMetadataAndParameters()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = Assert.Single(tools);

        Assert.Equal("weather", tool.Name);
        Assert.Equal("Gets weather for a location", tool.Description);
        Assert.Equal("ui://weather/view.html", tool.ProtocolTool.Meta?["ui"]?["resourceUri"]?.GetValue<string>());
        Assert.Contains("location", tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));

        var result = await client.CallToolAsync(
            "weather",
            new Dictionary<string, object?> { ["location"] = "Paris" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Equal("Weather for Paris", text.Text);
    }

    [Fact]
    public async Task WithAppTool_UsesAppMimeTypeAndFactoryContent()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var resource = Assert.Single(resources);
        Assert.Equal("ui://weather/view.html", resource.Uri);
        Assert.Equal(McpApps.HtmlMimeType, resource.MimeType);

        var result = await client.ReadResourceAsync(
            resource.Uri,
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal(resource.Uri, content.Uri);
        Assert.Equal(McpApps.HtmlMimeType, content.MimeType);
        Assert.Equal("<html><body>weather</body></html>", content.Text);
    }

    private static class AppTools
    {
        [McpServerTool(Name = "weather")]
        [Description("Gets weather for a location")]
        public static string GetWeather(string location) => $"Weather for {location}";
    }
}
