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
        .WithStreamServerTransport(Stream.Null, Stream.Null);

        await using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<McpServerOptions>>().Value;
        
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

    #region Registered Primitive Tests
    [Fact]
    public void Configure_RepeatedCreate_GivesEachOptionsItsOwnCollections()
    {
        var tool = McpServerTool.Create(() => "result", new() { Name = "tool" });
        var prompt = McpServerPrompt.Create(() => "prompt", new() { Name = "prompt" });
        var resource = McpServerResource.Create(() => "content", new() { UriTemplate = "test://resource", Name = "resource" });

        var services = new ServiceCollection();
        services.AddMcpServer().WithTools([tool]).WithPrompts([prompt]).WithResources([resource]);
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        var options1 = factory.Create(Options.DefaultName);
        var options2 = factory.Create(Options.DefaultName);

        Assert.NotSame(options1.ToolCollection, options2.ToolCollection);
        Assert.NotSame(options1.PromptCollection, options2.PromptCollection);
        Assert.NotSame(options1.ResourceCollection, options2.ResourceCollection);
        Assert.Same(tool, Assert.Single(options1.ToolCollection!));
        Assert.Same(tool, Assert.Single(options2.ToolCollection!));
        Assert.Same(prompt, Assert.Single(options2.PromptCollection!));
        Assert.Same(resource, Assert.Single(options2.ResourceCollection!));
    }

    [Fact]
    public void Configure_MutatingOneOptionsCollections_DoesNotAffectOtherOptions()
    {
        var tool = McpServerTool.Create(() => "result", new() { Name = "tool" });
        var resource = McpServerResource.Create(() => "content", new() { UriTemplate = "test://resource", Name = "resource" });

        var services = new ServiceCollection();
        services.AddMcpServer().WithTools([tool]).WithResources([resource]);
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        var options1 = factory.Create(Options.DefaultName);
        Assert.True(options1.ToolCollection!.Remove(tool));
        options1.ToolCollection.Add(McpServerTool.Create(() => "session", new() { Name = "sessionTool" }));
        options1.ResourceCollection!.Clear();

        var options2 = factory.Create(Options.DefaultName);

        Assert.Equal(["sessionTool"], options1.ToolCollection.PrimitiveNames);
        Assert.Empty(options1.ResourceCollection);
        Assert.Same(tool, Assert.Single(options2.ToolCollection!));
        Assert.Same(resource, Assert.Single(options2.ResourceCollection!));
    }

    [Fact]
    public void Configure_PreassignedCollection_IsKeptAndRegisteredPrimitivesAdded()
    {
        var registered = McpServerTool.Create(() => "result", new() { Name = "registered" });
        var preassigned = McpServerTool.Create(() => "result", new() { Name = "preassigned" });
        McpServerPrimitiveCollection<McpServerTool> collection = [preassigned];

        var services = new ServiceCollection();
        services.Configure<McpServerOptions>(options => options.ToolCollection = collection);
        services.AddMcpServer().WithTools([registered]);
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        for (int i = 0; i < 2; i++)
        {
            var options = factory.Create(Options.DefaultName);
            Assert.Same(collection, options.ToolCollection);
            Assert.Equal(["preassigned", "registered"], collection.PrimitiveNames);
        }
    }

    [Fact]
    public void Configure_DuplicateRegisteredNames_FirstRegistrationWins()
    {
        var first = McpServerTool.Create(() => "first", new() { Name = "tool" });
        var second = McpServerTool.Create(() => "second", new() { Name = "tool" });

        var services = new ServiceCollection();
        services.AddMcpServer().WithTools([first, second]);
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        for (int i = 0; i < 2; i++)
        {
            Assert.Same(first, Assert.Single(factory.Create(Options.DefaultName).ToolCollection!));
        }
    }

    [Fact]
    public void Configure_RegisteredPrimitiveRenamed_LaterOptionsUseNewName()
    {
        var tool = McpServerTool.Create(() => "result", new() { Name = "before" });

        var services = new ServiceCollection();
        services.AddMcpServer().WithTools([tool]);
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        var options1 = factory.Create(Options.DefaultName);
        tool.ProtocolTool.Name = "after";
        var options2 = factory.Create(Options.DefaultName);

        Assert.Equal(["before"], options1.ToolCollection!.PrimitiveNames);
        Assert.Equal(["after"], options2.ToolCollection!.PrimitiveNames);
        Assert.Same(tool, options2.ToolCollection["after"]);
    }

    [Fact]
    public void Configure_NoRegisteredPrimitives_LeavesCollectionsNull()
    {
        var services = new ServiceCollection();
        services.AddMcpServer();
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<IOptionsFactory<McpServerOptions>>();

        for (int i = 0; i < 2; i++)
        {
            var options = factory.Create(Options.DefaultName);
            Assert.Null(options.ToolCollection);
            Assert.Null(options.PromptCollection);
            Assert.Null(options.ResourceCollection);
        }
    }
    #endregion
}