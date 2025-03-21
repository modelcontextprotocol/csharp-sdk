using ModelContextProtocol.Configuration;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol;

/// <summary>
/// Extension to configure the MCP server with handlers
/// </summary>
public static partial class McpServerBuilderExtensions
{
    /// <summary>
    /// Sets the handler for list resource templates requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithListResourceTemplatesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourceTemplatesRequestParams>, CancellationToken, Task<ListResourceTemplatesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourceTemplatesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list tools requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithListToolsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListToolsRequestParams>, CancellationToken, Task<ListToolsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListToolsHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for call tool requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithCallToolHandler(this IMcpServerBuilder builder, Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<CallToolResponse>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.CallToolHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list prompts requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithListPromptsHandler(this IMcpServerBuilder builder, Func<RequestContext<ListPromptsRequestParams>, CancellationToken, Task<ListPromptsResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListPromptsHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for get prompt requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithGetPromptHandler(this IMcpServerBuilder builder, Func<RequestContext<GetPromptRequestParams>, CancellationToken, Task<GetPromptResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.GetPromptHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for list resources requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithListResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<ListResourcesRequestParams>, CancellationToken, Task<ListResourcesResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ListResourcesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for read resources requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithReadResourceHandler(this IMcpServerBuilder builder, Func<RequestContext<ReadResourceRequestParams>, CancellationToken, Task<ReadResourceResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.ReadResourceHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for get completion requests.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithGetCompletionHandler(this IMcpServerBuilder builder, Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.GetCompletionHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets the handler for subscribe to resources messages.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithSubscribeToResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<SubscribeRequestParams>, CancellationToken, Task<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.SubscribeToResourcesHandler = handler);
        return builder;
    }

    /// <summary>
    /// Sets or sets the handler for subscribe to resources messages.
    /// </summary>
    /// <param name="builder">The builder instance.</param>
    /// <param name="handler">The handler.</param>
    public static IMcpServerBuilder WithUnsubscribeFromResourcesHandler(this IMcpServerBuilder builder, Func<RequestContext<UnsubscribeRequestParams>, CancellationToken, Task<EmptyResult>> handler)
    {
        Throw.IfNull(builder);

        builder.Services.Configure<McpServerHandlers>(s => s.UnsubscribeFromResourcesHandler = handler);
        return builder;
    }
}
