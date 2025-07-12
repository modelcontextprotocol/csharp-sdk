using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Services;

/// <summary>
/// Service that handles route-aware tool filtering and automatic route discovery.
/// </summary>
/// <remarks>
/// <para>
/// This service maintains a registry of all available routes in the MCP server,
/// including the global route that serves all tools and specific routes that
/// serve filtered tool sets based on the <see cref="McpServerToolRouteAttribute"/>.
/// </para>
/// <para>
/// Routes are normalized to ensure consistent formatting and case-insensitive
/// comparison for reliable matching during request processing.
/// </para>
/// </remarks>
public class RouteAwareToolService
{
    private readonly HashSet<string> _otherRoutes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the base route for the MCP server.
    /// </summary>
    /// <remarks>
    /// The global route serves all available tools regardless of their route attributes.
    /// Multiple calls to <see cref="RegisterGlobalRoute"/> will overwrite the previous value.
    /// </remarks>
    public string? GlobalRoute { get; private set; }

    /// <summary>
    /// Gets all discovered routes from tool attributes.
    /// </summary>
    /// <remarks>
    /// This collection contains all routes discovered from <see cref="McpServerToolRouteAttribute"/>
    /// applied to tool methods, normalized for consistent comparison.
    /// </remarks>
    public IReadOnlySet<string> OtherRoutes => _otherRoutes;

    /// <summary>
    /// Registers routes with their associated tools.
    /// </summary>
    /// <param name="otherRoutes">Array of route paths to register. Null values are ignored.</param>
    /// <remarks>
    /// Routes are automatically normalized to ensure consistent formatting.
    /// Duplicate routes are ignored due to the underlying <see cref="HashSet{T}"/> implementation.
    /// </remarks>
    public void RegisterOtherRoutes(string[]? otherRoutes)
    {
        if (otherRoutes == null)
            return;

        foreach (string route in otherRoutes)
        {
            var normalizedRoute = NormalizePath(route);
            _otherRoutes.Add(normalizedRoute);
        }
    }

    /// <summary>
    /// Registers the global route that serves all tools.
    /// </summary>
    /// <param name="globalRoute">The global route path.</param>
    /// <remarks>
    /// Multiple calls to this method will overwrite the previous global route value,
    /// following Microsoft's convention of "last registration wins".
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="globalRoute"/> is null or whitespace.</exception>
    public void RegisterGlobalRoute(string globalRoute)
    {
        if (string.IsNullOrWhiteSpace(globalRoute))
            throw new ArgumentException("Global route cannot be null or empty.", nameof(globalRoute));

        GlobalRoute = NormalizePath(globalRoute);
    }

    /// <summary>
    /// Normalizes a path to ensure it follows consistent formatting rules.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>A normalized path with consistent formatting.</returns>
    /// <remarks>
    /// <para>Normalization rules:</para>
    /// <list type="bullet">
    /// <item><description>Null or whitespace paths become "/"</description></item>
    /// <item><description>Ensures paths start with "/"</description></item>
    /// <item><description>Removes trailing slashes except for root "/"</description></item>
    /// <item><description>Collapses multiple consecutive slashes to single slash</description></item>
    /// </list>
    /// </remarks>
    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        // Trim whitespace
        path = path.Trim();

        // Ensure it starts with a slash
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        // Remove trailing slashes (except for root "/")
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        // Handle multiple consecutive slashes
        while (path.Contains("//"))
        {
            path = path.Replace("//", "/");
        }

        return path;
    }
}