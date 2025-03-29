using ModelContextProtocol.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring <see cref="IMcpServer"/> instances.
/// </summary>
public interface IMcpServerBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }
}
