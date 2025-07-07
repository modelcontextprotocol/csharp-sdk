using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Server;

public class McpServerHandlerTests
{
    public McpServerHandlerTests()
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

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
        Assert.Null(handlers.CompleteHandler);
        Assert.Null(handlers.SubscribeToResourcesHandler);
        Assert.Null(handlers.UnsubscribeFromResourcesHandler);

        handlers.ListToolsHandler = async (p, c) => new ListToolsResult();
        handlers.CallToolHandler = async (p, c) => new CallToolResult();
        handlers.ListPromptsHandler = async (p, c) => new ListPromptsResult();
        handlers.GetPromptHandler = async (p, c) => new GetPromptResult();
        handlers.ListResourceTemplatesHandler = async (p, c) => new ListResourceTemplatesResult();
        handlers.ListResourcesHandler = async (p, c) => new ListResourcesResult();
        handlers.ReadResourceHandler = async (p, c) => new ReadResourceResult();
        handlers.CompleteHandler = async (p, c) => new CompleteResult();
        handlers.SubscribeToResourcesHandler = async (s, c) => new EmptyResult();
        handlers.UnsubscribeFromResourcesHandler = async (s, c) => new EmptyResult();

        Assert.NotNull(handlers.ListToolsHandler);
        Assert.NotNull(handlers.CallToolHandler);
        Assert.NotNull(handlers.ListPromptsHandler);
        Assert.NotNull(handlers.GetPromptHandler);
        Assert.NotNull(handlers.ListResourceTemplatesHandler);
        Assert.NotNull(handlers.ListResourcesHandler);
        Assert.NotNull(handlers.ReadResourceHandler);
        Assert.NotNull(handlers.CompleteHandler);
        Assert.NotNull(handlers.SubscribeToResourcesHandler);
        Assert.NotNull(handlers.UnsubscribeFromResourcesHandler);
    }
}
