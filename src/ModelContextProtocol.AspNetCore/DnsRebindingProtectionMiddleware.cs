using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Middleware that provides DNS rebinding protection for MCP servers by validating
/// Host and Origin headers on requests to localhost servers.
/// </summary>
/// <remarks>
/// <para>
/// DNS rebinding attacks can allow malicious websites to bypass browser same-origin policy
/// and make requests to localhost services. This middleware helps protect against such attacks
/// by validating that Host and Origin headers match expected localhost values.
/// </para>
/// <para>
/// Use <see cref="McpApplicationBuilderExtensions.UseMcpDnsRebindingProtection"/> to enable this middleware.
/// </para>
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="DnsRebindingProtectionMiddleware"/> class.
/// </remarks>
internal sealed partial class DnsRebindingProtectionMiddleware(
    RequestDelegate next,
    ILogger<DnsRebindingProtectionMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<DnsRebindingProtectionMiddleware> _logger = logger;

    /// <summary>
    /// Processes the HTTP request and validates Host and Origin headers for localhost servers.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply protection to localhost servers
        var localEndpoint = context.Connection.LocalIpAddress;
        bool isLocalhostServer = localEndpoint is null ||
                                 IPAddress.IsLoopback(localEndpoint) ||
                                 localEndpoint.Equals(IPAddress.IPv6Loopback);

        if (isLocalhostServer)
        {
            // Validate Host header
            var host = context.Request.Host.Host;
            if (!IsLocalhost(host))
            {
                LogInvalidHostHeader(host);
                await WriteJsonRpcErrorResponseAsync(context, $"Forbidden: Invalid Host header '{host}' for localhost server");
                return;
            }

            // Validate Origin header if present
            if (context.Request.Headers.TryGetValue(HeaderNames.Origin, out var originValues) &&
                originValues.FirstOrDefault() is string origin &&
                Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
                !IsLocalhost(originUri.Host))
            {
                LogInvalidOriginHeader(origin);
                await WriteJsonRpcErrorResponseAsync(context, $"Forbidden: Invalid Origin header '{origin}' for localhost server");
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsLocalhost(string host)
    {
        if (!string.IsNullOrWhiteSpace(host))
        {
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("[::1]") ||
                host.Equals("127.0.0.1"))
            {
                return true;
            }

            if (IPAddress.TryParse(host, out var ip))
            {
                return IPAddress.IsLoopback(ip);
            }
        }

        return false;
    }

    private static Task WriteJsonRpcErrorResponseAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync($$"""
            {
                "jsonrpc": "2.0",
                "error":
                {
                    "code": -32000,
                    "message": "{{JsonEncodedText.Encode(message)}}"
                },
                "id": null
            }
            """);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected request with invalid Host header '{Host}' for localhost server. This may indicate a DNS rebinding attack.")]
    private partial void LogInvalidHostHeader(string? host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected request with invalid Origin header '{Origin}' for localhost server. This may indicate a DNS rebinding attack.")]
    private partial void LogInvalidOriginHeader(string origin);
}
