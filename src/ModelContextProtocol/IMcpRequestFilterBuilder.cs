using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides a builder for configuring request-specific MCP server filters.
/// </summary>
public interface IMcpRequestFilterBuilder
{
    /// <summary>
    /// Gets the associated service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
