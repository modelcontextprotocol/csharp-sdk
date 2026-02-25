using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Client;

public class McpClientResourceTests : ClientServerTestBase
{
    public McpClientResourceTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithResources<SampleResources>();
    }

    [McpServerResourceType]
    private sealed class SampleResources
    {
        [McpServerResource, Description("A sample resource")]
        public static string Sample() => "Sample content";

        [McpServerResource, Description("Echoes back the metadata it receives")]
        public static string MetadataEcho(RequestContext<ReadResourceRequestParams> context) =>
            context.Params?.Meta?.ToJsonString() ?? "{}";
    }

    [Fact]
    public async Task Constructor_WithValidParameters_CreatesInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalResource = resources.First(r => r.Name == "sample");
        var resourceDefinition = originalResource.ProtocolResource;

        var newResource = new McpClientResource(client, resourceDefinition);

        Assert.NotNull(newResource);
        Assert.Equal("sample", newResource.Name);
        Assert.Equal("A sample resource", newResource.Description);
        Assert.Same(resourceDefinition, newResource.ProtocolResource);
    }

    [Fact]
    public void Constructor_WithNullClient_ThrowsArgumentNullException()
    {
        var resourceDefinition = new Resource
        {
            Uri = "file:///test.txt",
            Name = "test",
            Description = "Test resource"
        };

        Assert.Throws<ArgumentNullException>("client", () => new McpClientResource(null!, resourceDefinition));
    }

    [Fact]
    public async Task Constructor_WithNullResource_ThrowsArgumentNullException()
    {
        await using McpClient client = await CreateMcpClientForServer();

        Assert.Throws<ArgumentNullException>("resource", () => new McpClientResource(client, null!));
    }

    [Fact]
    public async Task ReuseResourceDefinition_AcrossDifferentClients_ReadsSuccessfully()
    {
        Resource resourceDefinition;
        {
            await using McpClient client1 = await CreateMcpClientForServer();
            var resources = await client1.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
            var sampleResource = resources.First(r => r.Name == "sample");
            resourceDefinition = sampleResource.ProtocolResource;
        }

        await using McpClient client2 = await CreateMcpClientForServer();

        var reusedResource = new McpClientResource(client2, resourceDefinition);

        var result = await reusedResource.ReadAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Contents);
        var content = result.Contents.FirstOrDefault() as TextResourceContents;
        Assert.NotNull(content);
        Assert.Equal("Sample content", content.Text);
    }

    [Fact]
    public async Task ReuseResourceDefinition_PreservesResourceMetadata()
    {
        await using McpClient client = await CreateMcpClientForServer();
        
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var originalResource = resources.First(r => r.Name == "sample");
        var resourceDefinition = originalResource.ProtocolResource;

        var reusedResource = new McpClientResource(client, resourceDefinition);

        Assert.Equal(originalResource.Name, reusedResource.Name);
        Assert.Equal(originalResource.Description, reusedResource.Description);
        Assert.Equal(originalResource.Uri, reusedResource.Uri);
        Assert.Equal(originalResource.ProtocolResource.Name, reusedResource.ProtocolResource.Name);
        Assert.Equal(originalResource.ProtocolResource.Description, reusedResource.ProtocolResource.Description);
    }

    [Fact]
    public async Task ManuallyConstructedResource_CreatesValidInstance()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var manualResource = new Resource
        {
            Uri = "file:///sample.txt",
            Name = "sample",
            Description = "A sample resource"
        };

        var clientResource = new McpClientResource(client, manualResource);

        Assert.NotNull(clientResource);
        Assert.Equal("sample", clientResource.Name);
        Assert.Equal("A sample resource", clientResource.Description);
        Assert.Equal("file:///sample.txt", clientResource.Uri);
    }

    [Fact]
    public async Task ReadAsync_WithRequestOptions_PassesMetaToServer()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        var resource = resources.Single(r => r.Name == "metadata_echo");

        RequestOptions requestOptions = new()
        {
            Meta = new JsonObject
            {
                ["traceId"] = "test-trace-123",
                ["customKey"] = "customValue"
            }
        };

        var result = await resource.ReadAsync(options: requestOptions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var content = Assert.IsType<TextResourceContents>(result.Contents.First());
        var receivedMetadata = JsonNode.Parse(content.Text)?.AsObject();
        Assert.NotNull(receivedMetadata);
        Assert.Equal("test-trace-123", receivedMetadata["traceId"]?.GetValue<string>());
        Assert.Equal("customValue", receivedMetadata["customKey"]?.GetValue<string>());
    }
}
