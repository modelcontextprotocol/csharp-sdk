using ModelContextProtocol;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Default implementation of <see cref="IMcpServerBuilder"/> that enables fluent configuration
/// of the Model Context Protocol (MCP) server. This builder is returned by the
/// <see cref="McpServerServiceCollectionExtensions.AddMcpServer"/> extension method and
/// provides access to the service collection for registering additional MCP components.
/// </summary>
internal sealed class DefaultMcpServerBuilder : IMcpServerBuilder
{
    /// <inheritdoc/>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMcpServerBuilder"/> class.
    /// </summary>
    /// <param name="services">The service collection to which MCP server services will be added. This collection
    /// is exposed through the <see cref="Services"/> property to allow additional configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public DefaultMcpServerBuilder(IServiceCollection services)
    {
        Throw.IfNull(services);

        Services = services;
    }
}
