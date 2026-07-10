#pragma warning disable MCPEXP003

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Round-trip integration tests for the MCP Apps <c>_meta.ui</c> metadata: tools registered
/// with <see cref="McpAppUiAttribute"/> and processed by <c>WithMcpApps()</c> are listed through
/// <see cref="McpClient.ListToolsAsync"/> and the structured <c>_meta.ui</c> object is verified
/// after serializing across the client-server transport.
/// </summary>
public class McpAppsIntegrationTests : ClientServerTestBase
{
    public McpAppsIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithTools<AppUiTools>()
            .WithMcpApps();
    }

    [Fact]
    public async Task ListToolsAsync_RoundTripsMetaUi_ForToolWithAppUiAttribute()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var weatherTool = Assert.Single(tools, t => t.Name == "weather_tool");

        JsonObject? uiNode = weatherTool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://weather/view.html", uiNode["resourceUri"]?.GetValue<string>());

        // Visibility was not restricted, so the attribute must not add the property.
        Assert.Null(uiNode["visibility"]);
    }

    [Fact]
    public async Task ListToolsAsync_RoundTripsMetaUiVisibility_ForRestrictedTool()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var modelOnlyTool = Assert.Single(tools, t => t.Name == "model_only_tool");

        JsonObject? uiNode = modelOnlyTool.ProtocolTool.Meta?["ui"]?.AsObject();
        Assert.NotNull(uiNode);
        Assert.Equal("ui://model-only/view.html", uiNode["resourceUri"]?.GetValue<string>());

        JsonArray? visibility = uiNode["visibility"]?.AsArray();
        Assert.NotNull(visibility);
        JsonNode? single = Assert.Single(visibility);
        Assert.Equal(McpUiToolVisibility.Model, single?.GetValue<string>());
    }

    [Fact]
    public async Task ListToolsAsync_HasNoUiMeta_ForToolWithoutAppUiAttribute()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var plainTool = Assert.Single(tools, t => t.Name == "plain_tool");

        Assert.Null(plainTool.ProtocolTool.Meta?["ui"]);
    }

    public sealed class AppUiTools
    {
        [McpServerTool(Name = "weather_tool")]
        [McpAppUi(ResourceUri = "ui://weather/view.html")]
        [Description("Get weather")]
        public static string WeatherTool(string location) => $"Weather for {location}";

        [McpServerTool(Name = "model_only_tool")]
        [McpAppUi(ResourceUri = "ui://model-only/view.html", Visibility = [McpUiToolVisibility.Model])]
        [Description("Model only")]
        public static string ModelOnlyTool(string location) => $"Model only for {location}";

        [McpServerTool(Name = "plain_tool")]
        [Description("Plain tool without app UI")]
        public static string PlainTool(string input) => input;
    }
}
