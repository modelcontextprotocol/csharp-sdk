using Microsoft.AspNetCore.Http;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Rejects requests whose <c>Origin</c> header cannot be verified against the server or its configured
/// allowed origins, protecting MCP endpoints from cross-origin browser requests (including DNS rebinding).
/// </summary>
/// <remarks>
/// A request is allowed when it has no <c>Origin</c> header (non-browser clients such as SDK clients and
/// <c>curl</c>), when the origin's host and port match the request's <c>Host</c> header, when the origin is a
/// loopback address (<c>localhost</c>, <c>127.0.0.1</c>, <c>[::1]</c>), or when the origin is listed in
/// <see cref="HttpServerTransportOptions.AllowedOrigins"/>. Any other request with an <c>Origin</c> header is
/// rejected with <c>403 Forbidden</c>.
/// </remarks>
internal sealed class OriginValidationEndpointFilter(HttpServerTransportOptions options) : IEndpointFilter
{
    /// <inheritdoc/>
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string? origin = context.HttpContext.Request.Headers.Origin;
        if (!string.IsNullOrEmpty(origin) && !IsOriginAllowed(context.HttpContext, origin))
        {
            return ValueTask.FromResult<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));
        }

        return next(context);
    }

    private bool IsOriginAllowed(HttpContext httpContext, string origin)
    {
        // Explicitly configured origins win and are matched exactly, case-insensitively, like the CORS middleware.
        foreach (string allowedOrigin in options.AllowedOrigins)
        {
            if (string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri) || originUri.Host.Length == 0)
        {
            return false;
        }

        // Loopback origins are always allowed so browsers running on the same machine (for example, a frontend
        // dev server on localhost:5173) can reach the server without extra configuration.
        if (IsLoopbackHost(originUri.Host))
        {
            return true;
        }

        // Allow the request when the origin's host and port match the request's Host header. The scheme is
        // intentionally not compared: TLS is often terminated by a reverse proxy, which leaves the request
        // scheme as http while the browser origin uses https.
        return HostMatchesRequest(httpContext.Request, originUri);
    }

    private static bool HostMatchesRequest(HttpRequest request, Uri originUri)
    {
        HostString requestHost = request.Host;
        if (requestHost.Host.Length == 0 || !string.Equals(requestHost.Host, originUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int requestPort = requestHost.Port ?? DefaultPort(request.Scheme);
        int originPort = originUri.IsDefaultPort ? DefaultPort(originUri.Scheme) : originUri.Port;
        return requestPort == originPort;
    }

    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           host is "127.0.0.1" or "::1" or "[::1]";

    private static int DefaultPort(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
