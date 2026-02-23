using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring message-level MCP server filters.
/// </summary>
public static class McpMessageFilterBuilderExtensions
{
    /// <summary>
    /// Adds a filter to intercept all incoming JSON-RPC messages.
    /// </summary>
    /// <param name="builder">The message filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the message handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpMessageFilterBuilder AddIncomingFilter(this IMcpMessageFilterBuilder builder, McpMessageFilter filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Message.IncomingFilters.Add(filter));
        return builder;
    }

    /// <summary>
    /// Adds a filter to intercept all outgoing JSON-RPC messages.
    /// </summary>
    /// <param name="builder">The message filter builder instance.</param>
    /// <param name="filter">The filter function that wraps the message handler.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpMessageFilterBuilder AddOutgoingFilter(this IMcpMessageFilterBuilder builder, McpMessageFilter filter)
    {
        Throw.IfNull(builder);
        Throw.IfNull(filter);

        builder.Services.Configure<McpServerOptions>(options => options.Filters.Message.OutgoingFilters.Add(filter));
        return builder;
    }
}
