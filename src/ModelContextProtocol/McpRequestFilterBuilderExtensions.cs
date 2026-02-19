using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring request-specific MCP server filters.
/// </summary>
public static class McpRequestFilterBuilderExtensions
{
    /// <summary>
    /// Adds a filter to the list resource templates handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddListResourceTemplatesFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.ListResourceTemplatesFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the list tools handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddListToolsFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<ListToolsRequestParams, ListToolsResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.ListToolsFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the call tool handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddCallToolFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<CallToolRequestParams, CallToolResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.CallToolFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the list prompts handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddListPromptsFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<ListPromptsRequestParams, ListPromptsResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.ListPromptsFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the get prompt handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddGetPromptFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<GetPromptRequestParams, GetPromptResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.GetPromptFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the list resources handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddListResourcesFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<ListResourcesRequestParams, ListResourcesResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.ListResourcesFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the read resource handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddReadResourceFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<ReadResourceRequestParams, ReadResourceResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.ReadResourceFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the complete handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddCompleteFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<CompleteRequestParams, CompleteResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.CompleteFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the subscribe-to-resources handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddSubscribeToResourcesFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<SubscribeRequestParams, EmptyResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.SubscribeToResourcesFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the unsubscribe-from-resources handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddUnsubscribeFromResourcesFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<UnsubscribeRequestParams, EmptyResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.UnsubscribeFromResourcesFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to the set logging level handler pipeline.
    /// </summary>
    /// <param name="builder">The request filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpRequestFilterBuilder AddSetLoggingLevelFilter(this IMcpRequestFilterBuilder builder, McpRequestFilter<SetLevelRequestParams, EmptyResult> filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Request.SetLoggingLevelFilters.Add(filter));
        return builder;
    }
}
