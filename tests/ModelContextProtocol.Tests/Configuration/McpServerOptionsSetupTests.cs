using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerOptionsSetupTests
{
    #region Prompt Handler Tests
    [Fact]
    public void Configure_WithListPromptsHandler_CreatesPromptsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListPromptsHandler(async (request, ct) => new ListPromptsResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListPromptsHandler);
        Assert.NotNull(options.Capabilities?.Prompts);
    }

    [Fact]
    public void Configure_WithGetPromptHandler_CreatesPromptsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithGetPromptHandler(async (request, ct) => new GetPromptResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.GetPromptHandler);
        Assert.NotNull(options.Capabilities?.Prompts);
    }
    #endregion

    #region Resource Handler Tests
    [Fact]
    public void Configure_WithListResourceTemplatesHandler_CreatesResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListResourceTemplatesHandler(async (request, ct) => new ListResourceTemplatesResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListResourceTemplatesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
    }

    [Fact]
    public void Configure_WithListResourcesHandler_CreatesResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListResourcesHandler(async (request, ct) => new ListResourcesResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListResourcesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
    }

    [Fact]
    public void Configure_WithReadResourceHandler_CreatesResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithReadResourceHandler(async (request, ct) => new ReadResourceResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ReadResourceHandler);
        Assert.NotNull(options.Capabilities?.Resources);
    }

    [Fact]
    public void Configure_WithSubscribeToResourcesHandler_And_WithOtherResourcesHandler_EnablesSubscription()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListResourcesHandler(async (request, ct) => new ListResourcesResult())
            .WithSubscribeToResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListResourcesHandler);
        Assert.NotNull(options.Handlers.SubscribeToResourcesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe);
    }

    [Fact]
    public void Configure_WithUnsubscribeFromResourcesHandler_And_WithOtherResourcesHandler_EnablesSubscription()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListResourcesHandler(async (request, ct) => new ListResourcesResult())
            .WithUnsubscribeFromResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListResourcesHandler);
        Assert.NotNull(options.Handlers.UnsubscribeFromResourcesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe);
    }

    [Fact]
    public void Configure_WithSubscribeToResourcesHandler_WithoutOtherResourcesHandler_DoesCreateResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithSubscribeToResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.SubscribeToResourcesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe);
    }

    [Fact]
    public void Configure_WithUnsubscribeFromResourcesHandler_WithoutOtherResourcesHandler_DoesCreateResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithUnsubscribeFromResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.UnsubscribeFromResourcesHandler);
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe);
    }

    [Fact]
    public void Configure_WithManualResourceSubscribeCapability_AndWithResources_PreservesCapabilityAndExposesResources()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            // User manually declares support for sending resource subscription notifications
            options.Capabilities = new()
            {
                Resources = new()
                {
                    Subscribe = true,
                }
            };
        })
        .WithResources<SimpleResourceType>();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;
        
        // The manually set capability should be preserved
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe, "User's manually set Subscribe capability should be preserved");
        
        // Resources should still be exposed
        Assert.NotNull(options.ResourceCollection);
        Assert.NotEmpty(options.ResourceCollection);
    }

    [Fact]
    public async Task ServerCapabilities_WithManualResourceSubscribeCapability_AndWithResources_ExposesSubscribeCapability()
    {
        // This test would require a full client-server setup, so we'll test via options validation instead
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            // User manually declares support for sending resource subscription notifications
            options.Capabilities = new()
            {
                Resources = new()
                {
                    Subscribe = true,
                    ListChanged = false, // explicitly set to false to test preservation
                }
            };
        })
        .WithResources<SimpleResourceType>()
        .WithStdioServerTransport();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;
        
        // The options should preserve the user's manually set capabilities
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe, "User's manually set Subscribe capability should be preserved in options");
        
        // ListChanged should be false as manually set (not overridden to true by resource collection logic in McpServerOptionsSetup)
        Assert.False(options.Capabilities.Resources.ListChanged, "User's manually set ListChanged capability should be preserved in options");
    }

    [Fact]
    public void Configure_WithManualResourceSubscribeCapability_WithoutWithResources_PreservesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer(options =>
        {
            // User manually declares support for sending resource subscription notifications
            options.Capabilities = new()
            {
                Resources = new()
                {
                    Subscribe = true,
                    ListChanged = true,
                }
            };
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;
        
        // The manually set capability should be preserved
        Assert.NotNull(options.Capabilities?.Resources);
        Assert.True(options.Capabilities.Resources.Subscribe);
        Assert.True(options.Capabilities.Resources.ListChanged);
    }
    #endregion

    [McpServerResourceType]
    public sealed class SimpleResourceType
    {
        [McpServerResource]
        public static string TestResource() => "Test content";
    }

    #region Tool Handler Tests
    [Fact]
    public void Configure_WithListToolsHandler_CreatesToolsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithListToolsHandler(async (request, ct) => new ListToolsResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.ListToolsHandler);
        Assert.NotNull(options.Capabilities?.Tools);
    }

    [Fact]
    public void Configure_WithCallToolHandler_CreatesToolsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithCallToolHandler(async (request, ct) => new CallToolResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.CallToolHandler);
        Assert.NotNull(options.Capabilities?.Tools);
    }
    #endregion

    #region Logging Handler Tests
    [Fact]
    public void Configure_WithSetLoggingLevelHandler_CreatesLoggingCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithSetLoggingLevelHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.SetLoggingLevelHandler);
        Assert.NotNull(options.Capabilities?.Logging);
    }
    #endregion

    #region Completion Handler Tests
    [Fact]
    public void Configure_WithCompleteHandler_CreatesCompletionsCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithCompleteHandler(async (request, ct) => new CompleteResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.Handlers.CompleteHandler);
        Assert.NotNull(options.Capabilities?.Completions);
    }
    #endregion

    #region TaskStore Tests
    [Fact]
    public void TaskStore_IsPopulatedFromDI_WhenNotExplicitlySet()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();
        services.AddSingleton<IMcpTaskStore, InMemoryMcpTaskStore>();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.IsType<InMemoryMcpTaskStore>(options.TaskStore);
    }

    [Fact]
    public void TaskStore_ExplicitOption_TakesPrecedenceOverDI()
    {
        var explicitStore = new InMemoryMcpTaskStore();

        var services = new ServiceCollection();
        services.AddMcpServer(options => options.TaskStore = explicitStore);
        services.AddSingleton<IMcpTaskStore, InMemoryMcpTaskStore>();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Same(explicitStore, options.TaskStore);
    }

    [Fact]
    public void TaskStore_RemainsNull_WhenNothingIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Null(options.TaskStore);
    }

    [Fact]
    public void TaskStore_CanBeOverriddenToNull_AfterDIRegistration()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();
        services.AddSingleton<IMcpTaskStore, InMemoryMcpTaskStore>();

        services.Configure<McpServerOptions>(options => options.TaskStore = null);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Null(options.TaskStore);
    }
    #endregion
}