using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for mapping MCP OAuth proxy endpoints.
/// </summary>
public static class McpOAuthProxyEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps OAuth proxy discovery, registration, authorization, callback, and token endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route prefix for operational OAuth endpoints.</param>
    /// <returns>The operational endpoint route group.</returns>
    public static RouteGroupBuilder MapMcpOAuthProxy(this IEndpointRouteBuilder endpoints, string pattern = "/oauth")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        if (!pattern.StartsWith('/') || pattern.Contains('{') || pattern.Contains('}'))
        {
            throw new ArgumentException("The OAuth proxy pattern must be a static absolute path.", nameof(pattern));
        }

        pattern = pattern.TrimEnd('/');
        var services = endpoints.ServiceProvider;
        var options = services.GetRequiredService<IOptions<McpOAuthProxyOptions>>().Value;
        var service = services.GetRequiredService<McpOAuthProxyService>();
        if (!string.Equals(options.Issuer.AbsolutePath.TrimEnd('/'), pattern, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("McpOAuthProxyOptions.Issuer path must match the MapMcpOAuthProxy route pattern.");
        }

        var issuerPath = pattern.Trim('/');
        var metadataPath = string.IsNullOrEmpty(issuerPath)
            ? "/.well-known/oauth-authorization-server"
            : $"/.well-known/oauth-authorization-server/{issuerPath}";
        RequestDelegate metadataHandler = context => service.HandleMetadataRequest().ExecuteAsync(context);
        endpoints.MapGet(metadataPath, metadataHandler).AllowAnonymous();

        var group = endpoints.MapGroup(pattern).AllowAnonymous();
        group.MapGet("/.well-known/openid-configuration", metadataHandler);
        group.MapPost("/register", ToRequestDelegate(service.HandleRegistrationAsync));
        group.MapGet("/authorize", ToRequestDelegate(service.HandleAuthorizationAsync));
        group.MapGet("/callback", ToRequestDelegate(service.HandleCallbackAsync));
        group.MapPost("/token", ToRequestDelegate(service.HandleTokenAsync));
        return group;
    }

    private static RequestDelegate ToRequestDelegate(Func<HttpContext, Task<IResult>> handler) =>
        async context =>
        {
            var result = await handler(context).ConfigureAwait(false);
            await result.ExecuteAsync(context).ConfigureAwait(false);
        };
}
