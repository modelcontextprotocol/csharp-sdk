using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsResourcesTests(ITestOutputHelper testOutputHelper)
    : ClientServerTestBase(testOutputHelper)
{
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder = mcpServerBuilder.WithResources(
            new FakeMcpServerResource()
            {
                ProtocolResource = new()
                {
                    Name = "test",
                    Uri = "test.txt",
                },
            },
            new FakeMcpServerResource()
            {
                ProtocolResource = new()
                {
                    Name = "test2",
                    Uri = "test2.txt",
                },
            });
        base.ConfigureServices(services, mcpServerBuilder);
    }

    private class FakeFileInfo : IFileInfo
    {
        public string Name { get; set; } = "test.txt";
        public long Length { get; set; } = 0;
        public bool Exists => true;

        public string? PhysicalPath { get; set; } = "test.txt";

        public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream();
    }

    private class FakeMcpServerResource : McpServerResource
    {
        public override required Resource ProtocolResource { get; init; } = new()
        {
            Name = "test",
            Uri = "test.txt",
        };

        public override Task<IFileInfo> GetFileInfoAsync(
            RequestContext<ReadResourceRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IFileInfo>(new FakeFileInfo()
            {
                Name = request?.Params?.Uri ?? "test.txt",
                PhysicalPath = request?.Params?.Uri ?? "test.txt",
                Length = 0,
            });
        }
    }

    [Fact]
    public void Adds_Resources_To_Server()
    {        
        var serverOptions = ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var resources = serverOptions?.Capabilities?.Resources?.ResourceCollection;
        Assert.NotNull(resources);
        Assert.Equal(2, resources.Count);
        Assert.Equal("test", resources["test"].ProtocolResource.Name);
        Assert.Equal("test2", resources["test2"].ProtocolResource.Name);
    }

    [Fact]
    public async Task Can_List_Resources()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var client = await CreateMcpClientForServer();

        // Act
        var resources = await client.ListResourcesAsync(token);

        // Assert
        Assert.NotNull(resources);
        Assert.Equal(2, resources.Count);
    }

    [Fact]
    public async Task Can_Be_Notified_Of_ResourceList_Changes()
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var client = await CreateMcpClientForServer();
        var serverOptions = ServiceProvider
            .GetRequiredService<IOptions<McpServerOptions>>()
            .Value;
        TaskCompletionSource<JsonRpcNotification> changeReceived = new();
        await using var _ = client.RegisterNotificationHandler(
            NotificationMethods.ResourceListChangedNotification,
            (notification, token) =>
            {
                changeReceived.SetResult(notification);
                return default;
            });

        // Act
        var resources = await client.ListResourcesAsync(token);
        Assert.NotNull(resources);
        Assert.Equal(2, resources.Count);

        serverOptions?.Capabilities?.Resources?.ResourceCollection?.Add(new FakeMcpServerResource
        {
            ProtocolResource = new()
            {
                Name = "new resource",
                Uri = "test3.txt",
            },
        });

        // Assert
        await changeReceived.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        var updatedResources = await client.ListResourcesAsync(token);
        Assert.NotNull(updatedResources);
        Assert.Equal(3, updatedResources.Count);
    }
}
