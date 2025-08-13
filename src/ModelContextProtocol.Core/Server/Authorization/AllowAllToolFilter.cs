using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// A tool filter that allows access to all tools without any restrictions.
/// </summary>
/// <remarks>
/// This filter is useful for development environments or scenarios where
/// no access control is required. It always returns authorization success
/// for any tool access request.
/// </remarks>
public sealed class AllowAllToolFilter : IToolFilter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AllowAllToolFilter"/> class.
    /// </summary>
    /// <param name="priority">The priority for this filter. Default is <see cref="int.MaxValue"/> (lowest priority).</param>
    public AllowAllToolFilter(int priority = int.MaxValue)
    {
        Priority = priority;
    }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <inheritdoc/>
    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Always include all tools
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Always allow execution
        return Task.FromResult(AuthorizationResult.Allow("All tools allowed"));
    }
}