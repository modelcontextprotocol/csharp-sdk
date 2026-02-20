using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides a builder for configuring message-level MCP server filters.
/// </summary>
public interface IMcpMessageFilterBuilder
{
    /// <summary>
    /// Gets the associated service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
