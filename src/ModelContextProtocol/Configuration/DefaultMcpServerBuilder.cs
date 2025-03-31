using ModelContextProtocol.Utils;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Default implementation of <see cref="IMcpServerBuilder"/>.
/// </summary>
internal sealed class DefaultMcpServerBuilder : IMcpServerBuilder
{
    /// <inheritdoc/>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultMcpServerBuilder"/> class.
    /// </summary>
    /// <param name="services"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public DefaultMcpServerBuilder(IServiceCollection services)
    {
        Throw.IfNull(services);

        Services = services;
    }
}
