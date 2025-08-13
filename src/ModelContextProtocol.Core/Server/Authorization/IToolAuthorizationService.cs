using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Defines the contract for tool authorization services that manage access control for MCP tools.
/// </summary>
/// <remarks>
/// The tool authorization service acts as the central orchestrator for tool filtering,
/// coordinating multiple tool filters and providing a unified interface for authorization decisions.
/// </remarks>
public interface IToolAuthorizationService
{
    /// <summary>
    /// Filters a collection of tools based on the current authorization context.
    /// </summary>
    /// <param name="tools">The collection of tools to filter.</param>
    /// <param name="context">The authorization context for the filtering operation.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains
    /// a filtered collection of tools that the current user is authorized to see.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tools"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method applies all registered tool filters to determine which tools
    /// should be visible to the requesting client. Tools are included only if
    /// all filters allow them.
    /// </remarks>
    Task<IEnumerable<Tool>> FilterToolsAsync(IEnumerable<Tool> tools, ToolAuthorizationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a specific tool can be executed by the current user.
    /// </summary>
    /// <param name="toolName">The name of the tool to authorize for execution.</param>
    /// <param name="context">The authorization context for the execution check.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains
    /// an <see cref="AuthorizationResult"/> indicating whether the tool execution is authorized.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolName"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method evaluates all registered tool filters to determine whether
    /// the specified tool can be executed. If any filter denies access, the
    /// authorization fails.
    /// </remarks>
    Task<AuthorizationResult> AuthorizeToolExecutionAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a tool filter with the authorization service.
    /// </summary>
    /// <param name="filter">The tool filter to register.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="filter"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Registered filters are executed in priority order when making authorization decisions.
    /// Multiple filters with the same priority may execute in any order.
    /// </remarks>
    void RegisterFilter(IToolFilter filter);

    /// <summary>
    /// Unregisters a tool filter from the authorization service.
    /// </summary>
    /// <param name="filter">The tool filter to unregister.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="filter"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// If the specified filter is not currently registered, this method has no effect.
    /// </remarks>
    void UnregisterFilter(IToolFilter filter);

    /// <summary>
    /// Gets all currently registered tool filters.
    /// </summary>
    /// <returns>A read-only collection of registered tool filters.</returns>
    IReadOnlyCollection<IToolFilter> GetRegisteredFilters();
}