using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// A tool filter that denies access to all tools.
/// </summary>
/// <remarks>
/// This filter is useful for lockdown scenarios or as a safety mechanism
/// to prevent any tool execution. It always returns authorization failure
/// for any tool access request.
/// </remarks>
public sealed class DenyAllToolFilter : IToolFilter
{
    private readonly string _reason;

    /// <summary>
    /// Initializes a new instance of the <see cref="DenyAllToolFilter"/> class.
    /// </summary>
    /// <param name="priority">The priority for this filter. Default is 0 (highest priority).</param>
    /// <param name="reason">The reason for denying access. Default is "All tools denied".</param>
    public DenyAllToolFilter(int priority = 0, string reason = "All tools denied")
    {
        Priority = priority;
        _reason = reason ?? "All tools denied";
    }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <inheritdoc/>
    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Never include any tools
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Always deny execution
        return Task.FromResult(AuthorizationResult.Deny(_reason));
    }
}