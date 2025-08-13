using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Default implementation of <see cref="IToolAuthorizationService"/> that coordinates multiple tool filters.
/// </summary>
/// <remarks>
/// This service manages a collection of tool filters and applies them in priority order
/// to make authorization decisions for tool visibility and execution.
/// </remarks>
internal sealed class ToolAuthorizationService : IToolAuthorizationService
{
    private readonly ConcurrentBag<IToolFilter> _filters = new();
    private readonly ILogger<ToolAuthorizationService>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolAuthorizationService"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    public ToolAuthorizationService(ILogger<ToolAuthorizationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolAuthorizationService"/> class with initial filters.
    /// </summary>
    /// <param name="filters">Initial collection of tool filters to register.</param>
    /// <param name="logger">Optional logger for diagnostic information.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="filters"/> is <see langword="null"/>.
    /// </exception>
    public ToolAuthorizationService(IEnumerable<IToolFilter> filters, ILogger<ToolAuthorizationService>? logger = null)
        : this(logger)
    {
        Throw.IfNull(filters);

        foreach (var filter in filters)
        {
            if (filter is not null)
            {
                _filters.Add(filter);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<Tool>> FilterToolsAsync(IEnumerable<Tool> tools, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(tools);
        Throw.IfNull(context);

        var filters = GetSortedFilters();
        if (filters.Count == 0)
        {
            _logger?.LogDebug("No tool filters registered, returning all tools");
            return tools;
        }

        var filteredTools = new List<Tool>();

        foreach (var tool in tools)
        {
            bool shouldInclude = true;

            foreach (var filter in filters)
            {
                try
                {
                    if (!await filter.ShouldIncludeToolAsync(tool, context, cancellationToken).ConfigureAwait(false))
                    {
                        shouldInclude = false;
                        _logger?.LogDebug("Tool '{ToolName}' filtered out by filter '{FilterType}'", tool.Name, filter.GetType().Name);
                        break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogError(ex, "Error in tool filter '{FilterType}' while evaluating tool '{ToolName}', denying access", filter.GetType().Name, tool.Name);
                    shouldInclude = false;
                    break;
                }
            }

            if (shouldInclude)
            {
                filteredTools.Add(tool);
                _logger?.LogDebug("Tool '{ToolName}' included after filtering", tool.Name);
            }
        }

        _logger?.LogInformation("Filtered {OriginalCount} tools to {FilteredCount} tools", tools.Count(), filteredTools.Count);
        return filteredTools;
    }

    /// <inheritdoc/>
    public async Task<AuthorizationResult> AuthorizeToolExecutionAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        }
        Throw.IfNull(context);

        var filters = GetSortedFilters();
        if (filters.Count == 0)
        {
            _logger?.LogDebug("No tool filters registered, allowing execution of tool '{ToolName}'", toolName);
            return AuthorizationResult.Allow("No filters configured");
        }

        foreach (var filter in filters)
        {
            try
            {
                var result = await filter.CanExecuteToolAsync(toolName, context, cancellationToken).ConfigureAwait(false);
                if (!result.IsAuthorized)
                {
                    _logger?.LogWarning("Tool execution denied for '{ToolName}' by filter '{FilterType}': {Reason}", 
                        toolName, filter.GetType().Name, result.Reason);
                    return result;
                }
                else
                {
                    _logger?.LogDebug("Tool execution allowed for '{ToolName}' by filter '{FilterType}'", 
                        toolName, filter.GetType().Name);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Error in tool filter '{FilterType}' while authorizing tool '{ToolName}', denying access", 
                    filter.GetType().Name, toolName);
                return AuthorizationResult.Deny($"Filter error: {ex.Message}");
            }
        }

        _logger?.LogInformation("Tool execution authorized for '{ToolName}' after all filters", toolName);
        return AuthorizationResult.Allow("All filters passed");
    }

    /// <inheritdoc/>
    public void RegisterFilter(IToolFilter filter)
    {
        Throw.IfNull(filter);

        _filters.Add(filter);
        _logger?.LogInformation("Registered tool filter '{FilterType}' with priority {Priority}", 
            filter.GetType().Name, filter.Priority);
    }

    /// <inheritdoc/>
    public void UnregisterFilter(IToolFilter filter)
    {
        Throw.IfNull(filter);

        // ConcurrentBag doesn't support removal, so we'll need to create a new collection
        // This is not the most efficient approach, but it's thread-safe and filters are typically
        // registered once during startup rather than dynamically during runtime
        var existingFilters = _filters.ToList();
        var newFilters = existingFilters.Where(f => !ReferenceEquals(f, filter)).ToList();

        if (existingFilters.Count != newFilters.Count)
        {
            // Clear and re-add the remaining filters
            while (_filters.TryTake(out _)) { }
            foreach (var remainingFilter in newFilters)
            {
                _filters.Add(remainingFilter);
            }

            _logger?.LogInformation("Unregistered tool filter '{FilterType}'", filter.GetType().Name);
        }
        else
        {
            _logger?.LogDebug("Tool filter '{FilterType}' was not found for unregistration", filter.GetType().Name);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<IToolFilter> GetRegisteredFilters()
    {
        return _filters.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets all registered filters sorted by priority.
    /// </summary>
    /// <returns>A list of filters sorted by priority (ascending order).</returns>
    private List<IToolFilter> GetSortedFilters()
    {
        return _filters.OrderBy(f => f.Priority).ToList();
    }
}