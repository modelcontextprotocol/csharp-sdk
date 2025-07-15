using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Provides extension methods for configuring MCP servers with route-aware tool filtering capabilities.
/// </summary>
/// <remarks>
/// <para>
/// This class extends the standard MCP configuration to support route-based tool filtering,
/// allowing different tool sets to be exposed at different URL paths based on the
/// <see cref="McpServerToolRouteAttribute"/> applied to tool methods.
/// </para>
/// <para>
/// Route-aware filtering enables scenarios where different contexts require different
/// tool capabilities, such as admin-only routes, read-only endpoints, or domain-specific
/// tool collections.
/// </para>
/// </remarks>
public static class McpRouteAwarenessExtension
{
    /// <summary>
    /// Configures the MCP server with HTTP transport and route-aware tool filtering capabilities.
    /// </summary>
    /// <param name="builder">The MCP server builder to configure.</param>
    /// <param name="configureOptions">Optional action to configure additional HTTP transport options.</param>
    /// <returns>The configured <see cref="IMcpServerBuilder"/> for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method enhances the standard HTTP transport with route-aware session configuration
    /// that filters available tools based on the requested URL path. Tools without route
    /// attributes are considered global and available on all routes.
    /// </para>
    /// <para>
    /// The filtering is applied per-session, ensuring that each client connection only
    /// sees tools appropriate for the requested route.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static IMcpServerBuilder WithHttpTransportAndRouting(this IMcpServerBuilder builder, Action<HttpServerTransportOptions>? configureOptions = null)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        // Configure HTTP transport with route-aware session options
        builder.WithHttpTransport(options =>
        {
            // Set up the route-aware session configuration
            options.ConfigureSessionOptions = async (httpContext, mcpOptions, cancellationToken) =>
            {
                var routeService = httpContext.RequestServices.GetService<RouteAwareToolService>();
                var requestPath = NormalizePath(httpContext.Request.Path.Value);

                // If no route service or requesting the global route, serve all tools
                if (routeService?.GlobalRoute is null || string.Equals(requestPath, routeService.GlobalRoute, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var toolCollection = mcpOptions.Capabilities?.Tools?.ToolCollection;
                if (toolCollection is null)
                {
                    return;
                }

                // Create a snapshot of current tools before filtering
                var allTools = toolCollection.ToList();
                toolCollection.Clear();

                // Filter tools based on the requested route
                foreach (var tool in allTools)
                {
                    if (ShouldIncludeTool(tool, requestPath, routeService))
                    {
                        toolCollection.Add(tool);
                    }
                }

                // Note: Resources and prompts are not filtered - they remain available on all routes
            };

            // Apply any additional configuration
            configureOptions?.Invoke(options);
        });

        // Register the route service as a singleton
        builder.Services.AddSingleton<RouteAwareToolService>();

        return builder;
    }

    /// <summary>
    /// Maps MCP endpoints with route-aware capabilities, creating endpoints for both the global route and all discovered tool routes.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder to configure.</param>
    /// <param name="pattern">The route pattern prefix for the global MCP endpoint.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for configuring additional endpoint conventions.</returns>
    /// <remarks>
    /// <para>
    /// This method discovers all tool routes from registered <see cref="McpServerTool"/> instances
    /// and creates MCP endpoints for each unique route. Tools without route attributes are
    /// available on the global route and all specific routes.
    /// </para>
    /// <para>
    /// For example, with a global pattern of "mcp" and tools having routes "admin" and "readonly":
    /// <list type="bullet">
    /// <item><description>/mcp - serves all tools (global route)</description></item>
    /// <item><description>/mcp/admin - serves admin-specific tools + global tools</description></item>
    /// <item><description>/mcp/readonly - serves readonly-specific tools + global tools</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    public static IEndpointConventionBuilder MapMcpWithRouting(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
    {
        if (endpoints is null)
            throw new ArgumentNullException(nameof(endpoints));

        var tools = endpoints.ServiceProvider.GetServices<McpServerTool>();
        var routeService = endpoints.ServiceProvider.GetService<RouteAwareToolService>();

        // Fallback to standard mapping if route service is not available
        if (routeService is null)
        {
            return endpoints.MapMcp(pattern);
        }

        // Normalize and register the global route
        var normalizedGlobalRoute = NormalizePath(pattern);
        routeService.RegisterGlobalRoute(normalizedGlobalRoute);

        // Discover and register all tool-specific routes
        foreach (var tool in tools)
        {
            if (tool.Routes?.Count > 0)
            {
                var toolRoutes = tool.Routes
                    .Select(route => NormalizePath($"{normalizedGlobalRoute}/{route}"))
                    .ToArray();

                routeService.RegisterOtherRoutes(toolRoutes);
            }
        }

        // Map MCP endpoints for all discovered routes
        foreach (var discoveredRoute in routeService.OtherRoutes)
        {
            endpoints.MapMcp(discoveredRoute);
        }

        // Map the global route and return its convention builder
        return endpoints.MapMcp(normalizedGlobalRoute);
    }

    /// <summary>
    /// Determines whether a tool should be included in the filtered tool collection for the specified route.
    /// </summary>
    /// <param name="tool">The tool to evaluate for inclusion.</param>
    /// <param name="requestPath">The requested URL path (normalized).</param>
    /// <param name="routeService">The route service containing route information.</param>
    /// <returns><see langword="true"/> if the tool should be included; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>A tool is included if:</para>
    /// <list type="bullet">
    /// <item><description>It has no route attributes (global tool), OR</description></item>
    /// <item><description>One of its routes matches the requested path</description></item>
    /// </list>
    /// </remarks>
    private static bool ShouldIncludeTool(McpServerTool tool, string requestPath, RouteAwareToolService routeService)
    {
        // Global tools (no routes) are always included
        if (tool.Routes is null || tool.Routes.Count == 0)
        {
            return true;
        }

        // Check if any of the tool's routes match the requested path
        return tool.Routes.Any(route =>
        {
            var fullToolPath = NormalizePath($"{routeService.GlobalRoute}/{route}");
            return string.Equals(fullToolPath, requestPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Normalizes a path to ensure consistent formatting for route comparison.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>A normalized path with consistent formatting.</returns>
    /// <remarks>
    /// This method applies the same normalization rules as <see cref="RouteAwareToolService"/>
    /// to ensure consistent path handling throughout the routing system.
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