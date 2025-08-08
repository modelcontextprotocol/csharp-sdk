﻿using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add MCP endpoints.
/// </summary>
public static class McpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Sets up endpoints for handling MCP Streamable HTTP transport.
    /// See <see href="https://modelcontextprotocol.io/specification/2025-06-18/basic/transports#streamable-http">the 2025-06-18 protocol specification</see> for details about the Streamable HTTP transport.
    /// </summary>
    /// <param name="endpoints">The web application to attach MCP HTTP endpoints.</param>
    /// <param name="pattern">The route pattern prefix to map to.</param>
    /// <returns>Returns a builder for configuring additional endpoint conventions like authorization policies.</returns>
    public static IEndpointConventionBuilder MapMcp(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern = ""
    )
    {
        var configurator =
            endpoints.ServiceProvider.GetService<IMcpEndpointRouteBuilderConfigurator>()
            ?? throw new InvalidOperationException(
                "You must call WithHttpTransport(). Unable to find required services. Call builder.Services.AddMcpServer().WithHttpTransport() in application startup code."
            );
        return configurator.Configure(endpoints, pattern);
    }
}
