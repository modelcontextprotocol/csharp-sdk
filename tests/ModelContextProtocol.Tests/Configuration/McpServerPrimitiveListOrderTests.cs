using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerPrimitiveListOrderTests : ClientServerTestBase
{
    public McpServerPrimitiveListOrderTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithTools<OrderedTools>()
            .WithPrompts<OrderedPrompts>()
            .WithResources<OrderedResources>();
    }

    [Fact]
    public async Task ListTools_ReturnsToolsInDeclarationOrder()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["zulu", "alpha", "mike", "echo", "yankee", "bravo", "kilo", "xray", "charlie", "lima"],
            tools.Select(t => t.Name));
    }

    [Fact]
    public async Task ListPrompts_ReturnsPromptsInDeclarationOrder()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["zulu", "alpha", "mike", "echo", "yankee", "bravo", "kilo", "xray", "charlie", "lima"],
            prompts.Select(p => p.Name));
    }

    [Fact]
    public async Task ListResources_ReturnsResourcesInDeclarationOrder()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["test://zulu", "test://alpha", "test://mike", "test://echo", "test://yankee", "test://bravo", "test://kilo", "test://xray"],
            resources.Select(r => r.Uri));
    }

    [Fact]
    public async Task ListResourceTemplates_ReturnsTemplatesInDeclarationOrder()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["test://zulu/{id}", "test://alpha/{id}", "test://mike/{id}", "test://echo/{id}", "test://yankee/{id}"],
            templates.Select(t => t.UriTemplate));
    }

    [McpServerToolType]
    public sealed class OrderedTools
    {
        [McpServerTool(Name = "zulu")] public static string Zulu() => "z";
        [McpServerTool(Name = "alpha")] public static string Alpha() => "a";
        [McpServerTool(Name = "mike")] public static string Mike() => "m";
        [McpServerTool(Name = "echo")] public static string Echo() => "e";
        [McpServerTool(Name = "yankee")] public static string Yankee() => "y";
        [McpServerTool(Name = "bravo")] public static string Bravo() => "b";
        [McpServerTool(Name = "kilo")] public static string Kilo() => "k";
        [McpServerTool(Name = "xray")] public static string Xray() => "x";
        [McpServerTool(Name = "charlie")] public static string Charlie() => "c";
        [McpServerTool(Name = "lima")] public static string Lima() => "l";
    }

    [McpServerPromptType]
    public sealed class OrderedPrompts
    {
        [McpServerPrompt(Name = "zulu")] public static string Zulu() => "z";
        [McpServerPrompt(Name = "alpha")] public static string Alpha() => "a";
        [McpServerPrompt(Name = "mike")] public static string Mike() => "m";
        [McpServerPrompt(Name = "echo")] public static string Echo() => "e";
        [McpServerPrompt(Name = "yankee")] public static string Yankee() => "y";
        [McpServerPrompt(Name = "bravo")] public static string Bravo() => "b";
        [McpServerPrompt(Name = "kilo")] public static string Kilo() => "k";
        [McpServerPrompt(Name = "xray")] public static string Xray() => "x";
        [McpServerPrompt(Name = "charlie")] public static string Charlie() => "c";
        [McpServerPrompt(Name = "lima")] public static string Lima() => "l";
    }

    [McpServerResourceType]
    public sealed class OrderedResources
    {
        [McpServerResource(UriTemplate = "test://zulu")] public static string Zulu() => "z";
        [McpServerResource(UriTemplate = "test://zulu/{id}")] public static string ZuluItem(string id) => id;
        [McpServerResource(UriTemplate = "test://alpha")] public static string Alpha() => "a";
        [McpServerResource(UriTemplate = "test://alpha/{id}")] public static string AlphaItem(string id) => id;
        [McpServerResource(UriTemplate = "test://mike")] public static string Mike() => "m";
        [McpServerResource(UriTemplate = "test://mike/{id}")] public static string MikeItem(string id) => id;
        [McpServerResource(UriTemplate = "test://echo")] public static string Echo() => "e";
        [McpServerResource(UriTemplate = "test://echo/{id}")] public static string EchoItem(string id) => id;
        [McpServerResource(UriTemplate = "test://yankee")] public static string Yankee() => "y";
        [McpServerResource(UriTemplate = "test://yankee/{id}")] public static string YankeeItem(string id) => id;
        [McpServerResource(UriTemplate = "test://bravo")] public static string Bravo() => "b";
        [McpServerResource(UriTemplate = "test://kilo")] public static string Kilo() => "k";
        [McpServerResource(UriTemplate = "test://xray")] public static string Xray() => "x";
    }
}
