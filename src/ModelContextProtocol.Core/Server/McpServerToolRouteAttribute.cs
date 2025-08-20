using System;
using System.Linq;

namespace ModelContextProtocol.Server;

/// <summary>
/// Specifies which HTTP route(s) this MCP tool should be available on.
/// This attribute is only used in AspNetCore scenarios and is ignored in stdio/console scenarios.
/// </summary>
/// <remarks>
/// <para>
/// This attribute works alongside the <see cref="McpServerToolAttribute"/> to provide
/// HTTP routing capabilities. Tool methods can have both routing and core tool attributes
/// to control where they are accessible via HTTP endpoints.
/// </para>
/// <para>
/// Tools without this attribute are considered global and available on all routes.
/// Multiple routes can be specified in a single attribute or by applying the attribute multiple times.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [McpServerTool, Description("Echoes the input back to the client.")]
/// [McpServerToolRoute("echo")]
/// public static string Echo(string message) { ... }
/// 
/// // Tool available on multiple routes
/// [McpServerTool, Description("Gets weather data")]
/// [McpServerToolRoute("weather", "utilities")]
/// public static string GetWeather(string location) { ... }
/// 
/// // Global tool (available everywhere)
/// [McpServerTool, Description("System diagnostics")]
/// public static string GetDiagnostics() { ... }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class McpServerToolRouteAttribute : Attribute
{
    /// <summary>
    /// Gets the route names this tool should be available on.
    /// </summary>
    /// <remarks>
    /// Route names are case-insensitive and will be matched against HTTP route segments.
    /// For example, a route name "echo" will be accessible at /mcp/echo when the global
    /// route is configured as "/mcp".
    /// </remarks>
    public IReadOnlyList<string> Routes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolRouteAttribute"/> class.
    /// </summary>
    /// <param name="routes">The route names this tool should be available on.</param>
    /// <exception cref="ArgumentException">Thrown when no routes are specified or any route is null/empty.</exception>
    public McpServerToolRouteAttribute(params string[] routes)
    {
        if (routes == null || routes.Length == 0)
            throw new ArgumentException("At least one route must be specified", nameof(routes));

        if (routes.Any(route => string.IsNullOrWhiteSpace(route)))
            throw new ArgumentException("Route names cannot be null or empty", nameof(routes));

        Routes = routes.Select(route => route.Trim()).ToArray();
    }
}