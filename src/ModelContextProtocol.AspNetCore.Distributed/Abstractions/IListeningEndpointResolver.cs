// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Hosting.Server;

namespace ModelContextProtocol.AspNetCore.Distributed.Abstractions;

/// <summary>
/// Resolves the listening endpoint address for the local server instance
/// that should be advertised to other instances for session affinity routing.
/// </summary>
public interface IListeningEndpointResolver
{
    /// <summary>
    /// Resolves the local server address that should be advertised to other instances
    /// for session affinity routing.
    /// </summary>
    /// <param name="server">The server instance to resolve addresses from.</param>
    /// <param name="options">Configuration options containing explicit address overrides.</param>
    /// <returns>A normalized address string in the format "scheme://host:port".</returns>
    /// <remarks>
    /// The resolution strategy is:
    /// <list type="number">
    /// <item><description>If <see cref="SessionAffinityOptions.LocalServerAddress"/> is set, validate and return it</description></item>
    /// <item><description>Otherwise, resolve from server bindings, preferring non-localhost HTTP addresses</description></item>
    /// <item><description>Fall back to http://localhost:80 if no addresses are available</description></item>
    /// </list>
    /// </remarks>
    string ResolveListeningEndpoint(IServer server, SessionAffinityOptions options);
}
