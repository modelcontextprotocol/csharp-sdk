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
    public void Configure_WithSubscribeToResourcesHandler_WithoutOtherResourcesHandler_DoesNotCreateResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithSubscribeToResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;
        
        Assert.Null(options.Handlers.SubscribeToResourcesHandler);
        Assert.Null(options.Capabilities?.Resources);
    }

    [Fact]
    public void Configure_WithUnsubscribeFromResourcesHandler_WithoutOtherResourcesHandler_DoesNotCreateResourcesCapability()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithUnsubscribeFromResourcesHandler(async (request, ct) => new EmptyResult());

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<McpServerOptions>>().Value;
        
        Assert.Null(options.Handlers.UnsubscribeFromResourcesHandler);
        Assert.Null(options.Capabilities?.Resources);
    }
    #endregion

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
}