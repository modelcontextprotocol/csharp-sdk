using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding an MCP OAuth proxy to an ASP.NET Core application.
/// </summary>
public static class McpOAuthProxyServiceCollectionExtensions
{
    internal const string HttpClientName = "ModelContextProtocol.AspNetCore.Authentication.McpOAuthProxy";

    /// <summary>
    /// Adds an OAuth authorization-server facade for an upstream provider that does not support
    /// Dynamic Client Registration.
    /// </summary>
    /// <remarks>
    /// An <see cref="IMcpOAuthProxyStore"/> must be registered. Single-instance applications may
    /// explicitly use <see cref="DistributedCacheMcpOAuthProxyStore"/>. Multi-instance deployments
    /// must provide a store whose consume operation is atomic in its backing store.
    /// </remarks>
    public static IServiceCollection AddMcpOAuthProxy(
        this IServiceCollection services,
        Action<McpOAuthProxyOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddDataProtection();
        services.AddHttpClient(HttpClientName);
        services.AddOptions<McpOAuthProxyOptions>()
            .Configure(configureOptions)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<McpOAuthProxyOptions>, McpOAuthProxyOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<McpOAuthProxyProtectedStore>();
        services.TryAddSingleton<McpOAuthProxyService>();
        return services;
    }
}
