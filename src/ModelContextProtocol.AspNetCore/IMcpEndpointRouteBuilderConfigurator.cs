using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Abstraction for configuring MCP endpoints on an <see cref="IEndpointRouteBuilder"/>.
/// Register an implementation in DI to override the default MapMcp behavior and enable hot swapping
/// of the transport/endpoints wiring without changing application code.
/// </summary>
public interface IMcpEndpointRouteBuilderConfigurator
{
    /// <summary>
    /// Configures the MCP endpoints on the provided <paramref name="endpoints"/> using the given <paramref name="pattern"/>.
    /// Implementations should return the <see cref="IEndpointConventionBuilder"/> representing the mapped group
    /// to allow callers to apply additional endpoint conventions (e.g., authorization).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to attach MCP endpoints to.</param>
    /// <param name="pattern">The route pattern prefix to map to.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> that can be used to configure conventions.</returns>
    IEndpointConventionBuilder Configure(IEndpointRouteBuilder endpoints, string pattern);
}
