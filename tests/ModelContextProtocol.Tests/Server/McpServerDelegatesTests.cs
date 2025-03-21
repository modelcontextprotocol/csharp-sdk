using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerHandlerTests
{
    [Fact]
    public void AllPropertiesAreSettable()
    {
        var handlers = new McpServerHandlers();

        Assert.Null(handlers.ListToolsHandler);
        Assert.Null(handlers.CallToolHandler);
        Assert.Null(handlers.ListPromptsHandler);
        Assert.Null(handlers.GetPromptHandler);
        Assert.Null(handlers.ListResourceTemplatesHandler);
        Assert.Null(handlers.ListResourcesHandler);
        Assert.Null(handlers.ReadResourceHandler);
        Assert.Null(handlers.GetCompletionHandler);
        Assert.Null(handlers.SubscribeToResourcesHandler);
        Assert.Null(handlers.UnsubscribeFromResourcesHandler);

        handlers.ListToolsHandler = (p, c) => Task.FromResult(new ListToolsResult());
        handlers.CallToolHandler = (p, c) => Task.FromResult(new CallToolResponse());
        handlers.ListPromptsHandler = (p, c) => Task.FromResult(new ListPromptsResult());
        handlers.GetPromptHandler = (p, c) => Task.FromResult(new GetPromptResult());
        handlers.ListResourceTemplatesHandler = (p, c) => Task.FromResult(new ListResourceTemplatesResult());
        handlers.ListResourcesHandler = (p, c) => Task.FromResult(new ListResourcesResult());
        handlers.ReadResourceHandler = (p, c) => Task.FromResult(new ReadResourceResult());
        handlers.GetCompletionHandler = (p, c) => Task.FromResult(new CompleteResult());
        handlers.SubscribeToResourcesHandler = (s, c) => Task.FromResult(new EmptyResult());
        handlers.UnsubscribeFromResourcesHandler = (s, c) => Task.FromResult(new EmptyResult());

        Assert.NotNull(handlers.ListToolsHandler);
        Assert.NotNull(handlers.CallToolHandler);
        Assert.NotNull(handlers.ListPromptsHandler);
        Assert.NotNull(handlers.GetPromptHandler);
        Assert.NotNull(handlers.ListResourceTemplatesHandler);
        Assert.NotNull(handlers.ListResourcesHandler);
        Assert.NotNull(handlers.ReadResourceHandler);
        Assert.NotNull(handlers.GetCompletionHandler);
        Assert.NotNull(handlers.SubscribeToResourcesHandler);
        Assert.NotNull(handlers.UnsubscribeFromResourcesHandler);
    }
}
