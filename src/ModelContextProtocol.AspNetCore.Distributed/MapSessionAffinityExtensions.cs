// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Extension methods for adding session affinity to MCP endpoints.
/// </summary>
public static class MapSessionAffinityExtensions
{
    /// <summary>
    /// Adds session affinity to MCP endpoints.
    /// This endpoint filter routes requests to the correct host based on session ownership.
    /// Use this on the return value of MapMcp() to add session affinity routing.
    /// Requires calling AddMcpHttpSessionAffinity() on the builder first.
    /// </summary>
    /// <param name="builder">The endpoint convention builder from MapMcp().</param>
    /// <returns>Returns the builder for chaining additional configurations.</returns>
    public static IEndpointConventionBuilder WithSessionAffinity(
        this IEndpointConventionBuilder builder
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilterFactory(
            (routeHandlerContext, next) =>
            {
                var filter =
                    routeHandlerContext.ApplicationServices.GetRequiredService<SessionAffinityEndpointFilter>();
                return (context) => filter.InvokeAsync(context, next);
            }
        );
        return builder;
    }
}
