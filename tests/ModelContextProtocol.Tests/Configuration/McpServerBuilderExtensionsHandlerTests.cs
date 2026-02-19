using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsHandlerTests
{
    private readonly Mock<IMcpServerBuilder> _builder;
    private readonly ServiceCollection _services;

    public McpServerBuilderExtensionsHandlerTests()
    {
        _services = new ServiceCollection();
        _builder = new Mock<IMcpServerBuilder>();
        _builder.SetupGet(b => b.Services).Returns(_services);
    }

    [Fact]
    public void WithListToolsHandler_Sets_Handler()
    {
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler = async (context, token) => new ListToolsResult();

        _builder.Object.WithListToolsHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.ListToolsHandler);
    }

    [Fact]
    public void WithCallToolHandler_Sets_Handler()
    {
        McpRequestHandler<CallToolRequestParams, CallToolResult> handler = async (context, token) => new CallToolResult();

        _builder.Object.WithCallToolHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.CallToolHandler);
    }

    [Fact]
    public void WithListPromptsHandler_Sets_Handler()
    {
        McpRequestHandler<ListPromptsRequestParams, ListPromptsResult> handler = async (context, token) => new ListPromptsResult();

        _builder.Object.WithListPromptsHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.ListPromptsHandler);
    }

    [Fact]
    public void WithGetPromptHandler_Sets_Handler()
    {
        McpRequestHandler<GetPromptRequestParams, GetPromptResult> handler = async (context, token) => new GetPromptResult();

        _builder.Object.WithGetPromptHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.GetPromptHandler);
    }

    [Fact]
    public void WithListResourceTemplatesHandler_Sets_Handler()
    {
        McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> handler = async (context, token) => new ListResourceTemplatesResult();

        _builder.Object.WithListResourceTemplatesHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.ListResourceTemplatesHandler);
    }

    [Fact]
    public void WithListResourcesHandler_Sets_Handler()
    {
        McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler = async (context, token) => new ListResourcesResult();

        _builder.Object.WithListResourcesHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.ListResourcesHandler);
    }

    [Fact]
    public void WithReadResourceHandler_Sets_Handler()
    {
        McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> handler = async (context, token) => new ReadResourceResult();

        _builder.Object.WithReadResourceHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.ReadResourceHandler);
    }

    [Fact]
    public void WithCompleteHandler_Sets_Handler()
    {
        McpRequestHandler<CompleteRequestParams, CompleteResult> handler = async (context, token) => new CompleteResult();

        _builder.Object.WithCompleteHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.CompleteHandler);
    }

    [Fact]
    public void WithSubscribeToResourcesHandler_Sets_Handler()
    {
        McpRequestHandler<SubscribeRequestParams, EmptyResult> handler = async (context, token) => new EmptyResult();

        _builder.Object.WithSubscribeToResourcesHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.SubscribeToResourcesHandler);
    }

    [Fact]
    public void WithUnsubscribeFromResourcesHandler_Sets_Handler()
    {
        McpRequestHandler<UnsubscribeRequestParams, EmptyResult> handler = async (context, token) => new EmptyResult();

        _builder.Object.WithUnsubscribeFromResourcesHandler(handler);

        var serviceProvider = _services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Equal(handler, options.Handlers.UnsubscribeFromResourcesHandler);
    }
}
