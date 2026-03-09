// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ModelContextProtocol.AspNetCore.Distributed.Abstractions;

namespace ModelContextProtocol.AspNetCore.Distributed;

/// <summary>
/// Default implementation of <see cref="IListeningEndpointResolver"/> that resolves
/// the local server listening endpoint from explicit configuration or server bindings.
/// </summary>
internal sealed class ListeningEndpointResolver : IListeningEndpointResolver
{
    /// <inheritdoc />
    public string ResolveListeningEndpoint(IServer server, SessionAffinityOptions options)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(options);

        // Use explicit configuration if provided
        if (!string.IsNullOrWhiteSpace(options.LocalServerAddress))
        {
            return ValidateAndNormalizeAddress(options.LocalServerAddress);
        }

        // Resolve from server bindings
        return ResolveFromServerBindings(server);
    }

    private static string ValidateAndNormalizeAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(
                $"LocalServerAddress '{address}' is not a valid absolute URI. "
                    + "It must include the scheme (http or https), host, and port (e.g., 'http://localhost:5000').",
                nameof(address)
            );
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"LocalServerAddress '{address}' must use either 'http' or 'https' scheme. "
                    + $"Got '{uri.Scheme}' instead.",
                nameof(address)
            );
        }

        // Normalize the address to include scheme, host, and port
        // Remove any path, query, or fragment components as they're not needed for forwarding
        var normalizedAddress = $"{uri.Scheme}://{uri.Host}:{uri.Port}";

        return normalizedAddress;
    }

    private static string ResolveFromServerBindings(IServer server)
    {
        var addressesFeature = server.Features.Get<IServerAddressesFeature>();
        if (addressesFeature is null || addressesFeature.Addresses.Count == 0)
        {
            // Fallback to http://localhost:80 if no addresses are available
            return "http://localhost:80";
        }

        Uri? httpUri = null;
        Uri? httpsUri = null;
        Uri? localhostHttpUri = null;
        Uri? localhostHttpsUri = null;

        foreach (var address in addressesFeature.Addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            {
                bool isLocalhost = IsLocalhostAddress(uri.Host);

                if (uri.Scheme == "http")
                {
                    if (isLocalhost)
                    {
                        localhostHttpUri ??= uri;
                    }
                    else
                    {
                        httpUri ??= uri;
                    }
                }
                else if (uri.Scheme == "https")
                {
                    if (isLocalhost)
                    {
                        localhostHttpsUri ??= uri;
                    }
                    else
                    {
                        httpsUri ??= uri;
                    }
                }
            }
        }

        // Prefer external interfaces over localhost for reachability from other servers
        // Prefer HTTP for internal routing in service mesh scenarios
        // In service meshes, internal traffic is typically HTTP while external is HTTPS
        var selectedUri = httpUri ?? httpsUri ?? localhostHttpUri ?? localhostHttpsUri;
        if (selectedUri is null)
        {
            // Fallback if no valid URI found
            return "http://localhost:80";
        }

        // Build address string in format "scheme://host:port"
        var host = selectedUri.Host;
        var port = selectedUri.Port;
        var scheme = selectedUri.Scheme;

        return $"{scheme}://{host}:{port}";
    }

    private static bool IsLocalhostAddress(string host)
    {
        // Check for common localhost representations
        if (
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal)
            || string.Equals(host, "[::1]", StringComparison.Ordinal)
        )
        {
            return true;
        }

        // Try to parse as IP address and check if it's loopback
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IPAddress.IsLoopback(ipAddress);
        }

        return false;
    }
}
