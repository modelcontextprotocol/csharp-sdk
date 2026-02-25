using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientResourceTemplateConstructorTests : ClientServerTestBase
{
    public McpClientResourceTemplateConstructorTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithResources<FileTemplateResources>();
    }

    [McpServerResourceType]
    private sealed class FileTemplateResources
    {
        [McpServerResource, Description("A file template")]
        public static string FileTemplate([Description("The file path")] string path) => $"Content for {path}";

        [McpServerResource, Description("Echoes back the metadata it receives")]
        public static string MetadataEcho(RequestContext<ReadResourceRequestParams> context, [Description("An ID")] string id) =>
            context.Params?.Meta?.ToJsonString() ?? "{}";
    }

    [Fact]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalTemplate = templates.First(t => t.Name == "file_template");
        var templateDefinition = originalTemplate.ProtocolResourceTemplate;

        var newTemplate = new McpClientResourceTemplate(client, templateDefinition);

        Assert.NotNull(newTemplate);
        Assert.Equal("file_template", newTemplate.Name);
        Assert.Equal("A file template", newTemplate.Description);
        Assert.Same(templateDefinition, newTemplate.ProtocolResourceTemplate);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        var templateDefinition = new ResourceTemplate
        {
            UriTemplate = "file:///{path}",
            Name = "test",
            Description = "Test template"
        };

        Assert.Throws<ArgumentNullException>("client", () => new McpClientResourceTemplate(null!, templateDefinition));
    }

    [Fact]
    public async Task Constructor_WithNullResourceTemplate_ThrowsArgumentNullException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        Assert.Throws<ArgumentNullException>("resourceTemplate", () => new McpClientResourceTemplate(client, null!));
    }

    [Fact]
    public async Task ReuseResourceTemplateDefinition_PreservesTemplateMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();
        
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalTemplate = templates.First(t => t.Name == "file_template");
        var templateDefinition = originalTemplate.ProtocolResourceTemplate;

        var reusedTemplate = new McpClientResourceTemplate(client, templateDefinition);

        Assert.Equal(originalTemplate.Name, reusedTemplate.Name);
        Assert.Equal(originalTemplate.Description, reusedTemplate.Description);
        Assert.Equal(originalTemplate.UriTemplate, reusedTemplate.UriTemplate);
        Assert.Equal(originalTemplate.ProtocolResourceTemplate.Name, reusedTemplate.ProtocolResourceTemplate.Name);
        Assert.Equal(originalTemplate.ProtocolResourceTemplate.Description, reusedTemplate.ProtocolResourceTemplate.Description);
    }

    [Fact]
    public async Task ManuallyConstructedResourceTemplate_CreatesValidInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var manualTemplate = new ResourceTemplate
        {
            UriTemplate = "file:///{path}",
            Name = "file_template",
            Description = "A file template"
        };

        var clientTemplate = new McpClientResourceTemplate(client, manualTemplate);

        Assert.NotNull(clientTemplate);
        Assert.Equal("file_template", clientTemplate.Name);
        Assert.Equal("A file template", clientTemplate.Description);
        Assert.Equal("file:///{path}", clientTemplate.UriTemplate);
    }

    [Fact]
    public async Task ReadAsync_WithRequestOptions_PassesMetaToServer()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var template = templates.Single(t => t.Name == "metadata_echo");

        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["traceId"] = "test-trace-123",
                ["customKey"] = "customValue"
            }
        };

        var result = await template.ReadAsync(
            new Dictionary<string, object?> { ["id"] = "test-id" },
            options: requestOptions,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var content = Assert.IsType<TextResourceContents>(result.Contents.First());
        var receivedMetadata = JsonNode.Parse(content.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("test-trace-123", receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Equal("customValue", receivedMetadata["customKey"]?.GetValue<string>());
    }
}
