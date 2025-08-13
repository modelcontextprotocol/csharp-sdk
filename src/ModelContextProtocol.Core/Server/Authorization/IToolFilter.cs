using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Defines the contract for implementing tool filtering logic in MCP servers.
/// </summary>
/// <remarks>
/// Tool filters allow MCP servers to control which tools are visible and accessible
/// to specific clients or user contexts. This enables fine-grained access control
/// and authorization for tool operations.
/// </remarks>
public interface IToolFilter
{
    /// <summary>
    /// Determines whether a specific tool should be included in the list of available tools.
    /// </summary>
    /// <param name="tool">The tool to evaluate for inclusion.</param>
    /// <param name="context">The authorization context containing user and session information.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains
    /// <see langword="true"/> if the tool should be included; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tool"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method is called during tool listing operations to determine which tools
    /// should be visible to the requesting client. Implementations should perform
    /// authorization checks based on the provided context.
    /// </remarks>
    Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a specific tool can be executed by the current user context.
    /// </summary>
    /// <param name="toolName">The name of the tool to authorize for execution.</param>
    /// <param name="context">The authorization context containing user and session information.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains
    /// an <see cref="AuthorizationResult"/> indicating whether the operation is authorized.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolName"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This method is called before tool execution to ensure the user has permission
    /// to invoke the specified tool. Implementations should perform comprehensive
    /// authorization checks and return detailed failure reasons when access is denied.
    /// </remarks>
    Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the priority order for this filter when multiple filters are registered.
    /// </summary>
    /// <value>
    /// An integer representing the execution priority. Lower values indicate higher priority.
    /// Filters with the same priority may execute in any order.
    /// </value>
    /// <remarks>
    /// When multiple tool filters are registered, they are executed in priority order.
    /// Higher priority filters (lower numeric values) are evaluated first. If any filter
    /// denies access, the operation is rejected regardless of lower priority filter results.
    /// </remarks>
    int Priority { get; }
}