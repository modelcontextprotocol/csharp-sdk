// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel.DataAnnotations;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace ModelContextProtocol.AspNetCore.Distributed.Abstractions;

/// <summary>
/// Configuration options for MCP session affinity routing behavior.
/// </summary>
public sealed class SessionAffinityOptions
{
    /// <summary>
    /// Configuration for the YARP forwarder when routing requests to other silos.
    /// If not set, a default configuration will be used.
    /// </summary>
    public ForwarderRequestConfig? ForwarderRequestConfig { get; set; }

    /// <summary>
    /// Configuration for the HTTP client used when forwarding requests to other silos.
    /// If not set, an empty configuration will be used.
    /// </summary>
    public HttpClientConfig? HttpClientConfig { get; set; }

    /// <summary>
    /// The service key to use when resolving the <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> service.
    /// When set, the session store will use a keyed HybridCache service that can be configured
    /// to use a specific distributed cache backend (e.g., Redis, SQL Server).
    /// This enables scenarios where multiple cache instances are needed in a single application.
    /// </summary>
    /// <remarks>
    /// This property is used in conjunction with keyed HybridCache registration.
    /// Register a keyed HybridCache instance using the standard DI keyed services APIs.
    /// </remarks>
    public string? HybridCacheServiceKey { get; set; }

    /// <summary>
    /// Explicitly sets the local server address that will be advertised to other instances
    /// for session affinity routing. This address is stored in the distributed session store
    /// and used by other servers to forward requests back to this instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set, this value takes precedence over automatic address resolution from server bindings.
    /// This is useful in scenarios where:
    /// <list type="bullet">
    /// <item><description>Running in containerized environments where internal addresses differ from advertised addresses</description></item>
    /// <item><description>Using service meshes where specific addresses/ports must be used for routing</description></item>
    /// <item><description>Multiple network interfaces are available and a specific one should be used</description></item>
    /// <item><description>Running behind load balancers or proxies with address translation</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The value must be a valid absolute URI including scheme (http or https), host, and port.
    /// Examples:
    /// <list type="bullet">
    /// <item><description><c>http://pod-1.mcp-service.default.svc.cluster.local:8080</c></description></item>
    /// <item><description><c>http://10.0.1.5:5000</c></description></item>
    /// <item><description><c>https://server1.internal:443</c></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If not set, the address will be automatically resolved from the server's configured
    /// bindings, preferring HTTP over HTTPS for service mesh scenarios.
    /// </para>
    /// </remarks>
    [HttpOrHttpsUri]
    public string? LocalServerAddress { get; set; }
}

/// <summary>
/// Validates that a string is a valid HTTP or HTTPS URI.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
internal sealed class HttpOrHttpsUriAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null or string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        if (value is not string stringValue)
        {
            return new ValidationResult("Value must be a string.");
        }

        if (!Uri.TryCreate(stringValue, UriKind.Absolute, out Uri? uri))
        {
            return new ValidationResult($"'{stringValue}' is not a valid absolute URI.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new ValidationResult($"URI must use HTTP or HTTPS scheme. Found: {uri.Scheme}");
        }

        return ValidationResult.Success;
    }
}
