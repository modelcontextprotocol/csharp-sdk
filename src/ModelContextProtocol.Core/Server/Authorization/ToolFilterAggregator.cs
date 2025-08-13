using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Aggregates multiple tool filters from dependency injection and applies them collectively.
/// </summary>
/// <remarks>
/// This class automatically discovers and manages tool filters registered in the dependency
/// injection container, providing a convenient way to apply multiple filters without manual registration.
/// </remarks>
internal sealed class ToolFilterAggregator : IToolFilter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolFilterAggregator>? _logger;
    private IToolFilter[]? _cachedFilters;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolFilterAggregator"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve tool filters from.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    public ToolFilterAggregator(IServiceProvider serviceProvider, ILogger<ToolFilterAggregator>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;
    }

    /// <inheritdoc/>
    public int Priority => int.MinValue; // Aggregator runs with highest priority to coordinate other filters

    /// <inheritdoc/>
    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(tool);
        Throw.IfNull(context);

        var filters = GetFilters();
        if (filters.Length == 0)
        {
            _logger?.LogDebug("No tool filters found in DI container, allowing tool '{ToolName}'", tool.Name);
            return true;
        }

        foreach (var filter in filters)
        {
            try
            {
                if (!await filter.ShouldIncludeToolAsync(tool, context, cancellationToken).ConfigureAwait(false))
                {
                    _logger?.LogDebug("Tool '{ToolName}' filtered out by aggregated filter '{FilterType}'", 
                        tool.Name, filter.GetType().Name);
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error in aggregated tool filter '{FilterType}' while evaluating tool '{ToolName}', denying access", 
                    filter.GetType().Name, tool.Name);
                return false;
            }
        }

        _logger?.LogDebug("Tool '{ToolName}' passed all aggregated filters", tool.Name);
        return true;
    }

    /// <inheritdoc/>
    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        }
        Throw.IfNull(context);

        var filters = GetFilters();
        if (filters.Length == 0)
        {
            _logger?.LogDebug("No tool filters found in DI container, allowing execution of tool '{ToolName}'", toolName);
            return AuthorizationResult.Allow("No filters configured");
        }

        foreach (var filter in filters)
        {
            try
            {
                var result = await filter.CanExecuteToolAsync(toolName, context, cancellationToken).ConfigureAwait(false);
                if (!result.IsAuthorized)
                {
                    _logger?.LogWarning("Tool execution denied for '{ToolName}' by aggregated filter '{FilterType}': {Reason}", 
                        toolName, filter.GetType().Name, result.Reason);
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error in aggregated tool filter '{FilterType}' while authorizing tool '{ToolName}', denying access", 
                    filter.GetType().Name, toolName);
                return AuthorizationResult.Deny($"Filter error in {filter.GetType().Name}: {ex.Message}");
            }
        }

        _logger?.LogInformation("Tool execution authorized for '{ToolName}' by all aggregated filters", toolName);
        return AuthorizationResult.Allow("All aggregated filters passed");
    }

    /// <summary>
    /// Gets all tool filters from the dependency injection container, sorted by priority.
    /// </summary>
    /// <returns>An array of tool filters sorted by priority.</returns>
    private IToolFilter[] GetFilters()
    {
        if (_cachedFilters is not null)
        {
            return _cachedFilters;
        }

        try
        {
            var filters = _serviceProvider.GetServices<IToolFilter>()
                .Where(f => f != this) // Exclude self to avoid infinite recursion
                .OrderBy(f => f.Priority)
                .ToArray();

            _cachedFilters = filters;
            _logger?.LogDebug("Discovered {FilterCount} tool filters from DI container", filters.Length);

            return filters;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error resolving tool filters from DI container");
            return Array.Empty<IToolFilter>();
        }
    }

    /// <summary>
    /// Clears the cached filters, forcing them to be re-resolved on the next access.
    /// </summary>
    /// <remarks>
    /// This method can be useful in scenarios where filters are registered dynamically
    /// and the aggregator needs to pick up the changes.
    /// </remarks>
    public void ClearCache()
    {
        _cachedFilters = null;
        _logger?.LogDebug("Cleared tool filter cache");
    }
}