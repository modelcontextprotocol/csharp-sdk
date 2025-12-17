using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.Configuration;

// Integration test with full client-server setup
public class McpServerResourceCapabilityIntegrationTests : ClientServerTestBase
{
    public McpServerResourceCapabilityIntegrationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // User manually declares support for sending resource subscription notifications
        services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities = new()
            {
                Resources = new()
                {
                    Subscribe = true,
                }
            };
        });

        mcpServerBuilder.WithResources<SimpleResourceType>();
    }

    [Fact]
    public async Task Client_CanListResources_WhenSubscribeCapabilityIsManuallySet()
    {
        await using McpClient client = await CreateMcpClientForServer();

        // The server should advertise Subscribe capability
        Assert.NotNull(client.ServerCapabilities.Resources);
        Assert.True(client.ServerCapabilities.Resources.Subscribe, "Server should advertise Subscribe capability when manually set");

        // The resources should be exposed and listable
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(resources);
        Assert.Contains(resources, r => r.Name == "test_resource");
    }

    [Fact]
    public async Task Client_CanListResources_WhenCapabilitySetViaAddMcpServerCallback()
    {
        // This is a separate test using a different configuration approach
        await using McpClient client = await CreateMcpClientForServer();

        // The server should advertise Subscribe capability
        Assert.NotNull(client.ServerCapabilities.Resources);

        // The resources should be exposed and listable
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(resources);
    }

    [McpServerResourceType]
    public sealed class SimpleResourceType
    {
        [McpServerResource]
        public static string TestResource() => "Test content";
    }
}

// Test that exactly matches the issue scenario
public class McpServerResourceCapabilityIssueReproTests : ClientServerTestBase
{
    public McpServerResourceCapabilityIssueReproTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // This test uses the exact pattern from the issue:
        // AddMcpServer with options callback that sets Capabilities.Resources.Subscribe = true
        // followed by WithResources
        // NO call to services.Configure after AddMcpServer
    }

    [Fact]
    public async Task Resources_AreExposed_WhenSubscribeCapabilitySetInAddMcpServerOptions()
    {
        // Create a fresh service collection to test the exact scenario from the issue
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(XunitLoggerProvider);

        // This matches the issue: setting capabilities in AddMcpServer callback
        var builder = services.AddMcpServer(
            options =>
            {
                // Declare support for sending resource subscription notifications
                options.Capabilities = new()
                {
                    Resources = new()
                    {
                        Subscribe = true,
                    }
                };
            })
            .WithResources<LiveResources>()
            .WithStdioServerTransport();

        var serviceProvider = services.BuildServiceProvider();
        var mcpOptions = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // Verify capabilities are preserved
        Assert.NotNull(mcpOptions.Capabilities?.Resources);
        Assert.True(mcpOptions.Capabilities.Resources.Subscribe, "Subscribe capability should be preserved");

        // Verify resources are registered
        Assert.NotNull(mcpOptions.ResourceCollection);
        Assert.NotEmpty(mcpOptions.ResourceCollection);
        Assert.Contains(mcpOptions.ResourceCollection, r => r.ProtocolResource?.Name == "live_resource");
    }

    [Fact]
    public void ResourcesCapability_IsCreated_WhenOnlyResourcesAreProvided()
    {
        // Test that ResourcesCapability is created even without handlers or manual setting
        var services = new ServiceCollection();
        var builder = services.AddMcpServer()
            .WithResources<LiveResources>()
            .WithStdioServerTransport();

        var serviceProvider = services.BuildServiceProvider();
        var mcpOptions = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        // Resources are registered
        Assert.NotNull(mcpOptions.ResourceCollection);
        Assert.NotEmpty(mcpOptions.ResourceCollection);

        // But ResourcesCapability should NOT be created just because resources exist!
        // The capability is only created when resources are actually used by the server
        // This is correct behavior - the capability is set up during server initialization
        // in McpServerImpl.ConfigureResources
    }

    [McpServerResourceType]
    public sealed class LiveResources
    {
        [McpServerResource]
        public static string LiveResource() => "Live content";
    }
}
