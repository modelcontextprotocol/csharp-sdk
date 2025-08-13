using ModelContextProtocol.Protocol;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// A tool filter that allows or denies access based on tool name patterns.
/// </summary>
/// <remarks>
/// This filter uses regular expressions or simple string matching to determine
/// which tools should be accessible. It supports both allow-list and deny-list patterns.
/// </remarks>
public sealed class ToolNamePatternFilter : IToolFilter
{
    private readonly List<Regex> _allowPatterns;
    private readonly List<Regex> _denyPatterns;
    private readonly bool _defaultAllow;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolNamePatternFilter"/> class.
    /// </summary>
    /// <param name="priority">The priority for this filter.</param>
    /// <param name="defaultAllow">
    /// Whether to allow access by default when no patterns match.
    /// If <see langword="true"/>, tools are allowed unless explicitly denied.
    /// If <see langword="false"/>, tools are denied unless explicitly allowed.
    /// </param>
    public ToolNamePatternFilter(int priority = 100, bool defaultAllow = false)
    {
        Priority = priority;
        _defaultAllow = defaultAllow;
        _allowPatterns = new List<Regex>();
        _denyPatterns = new List<Regex>();
    }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <summary>
    /// Adds a pattern that allows access to matching tool names.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to match tool names.</param>
    /// <param name="options">Optional regex options. Default is case-insensitive.</param>
    /// <returns>This filter instance for method chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="pattern"/> is null or empty.
    /// </exception>
    public ToolNamePatternFilter Allow(string pattern, RegexOptions options = RegexOptions.IgnoreCase)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

        _allowPatterns.Add(new Regex(pattern, options | RegexOptions.Compiled));
        return this;
    }

    /// <summary>
    /// Adds a pattern that denies access to matching tool names.
    /// </summary>
    /// <param name="pattern">The regular expression pattern to match tool names.</param>
    /// <param name="options">Optional regex options. Default is case-insensitive.</param>
    /// <returns>This filter instance for method chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="pattern"/> is null or empty.
    /// </exception>
    public ToolNamePatternFilter Deny(string pattern, RegexOptions options = RegexOptions.IgnoreCase)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));

        _denyPatterns.Add(new Regex(pattern, options | RegexOptions.Compiled));
        return this;
    }

    /// <summary>
    /// Adds multiple patterns that allow access to matching tool names.
    /// </summary>
    /// <param name="patterns">The regular expression patterns to match tool names.</param>
    /// <param name="options">Optional regex options. Default is case-insensitive.</param>
    /// <returns>This filter instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="patterns"/> is null.
    /// </exception>
    public ToolNamePatternFilter AllowMany(IEnumerable<string> patterns, RegexOptions options = RegexOptions.IgnoreCase)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                Allow(pattern, options);
            }
        }

        return this;
    }

    /// <summary>
    /// Adds multiple patterns that deny access to matching tool names.
    /// </summary>
    /// <param name="patterns">The regular expression patterns to match tool names.</param>
    /// <param name="options">Optional regex options. Default is case-insensitive.</param>
    /// <returns>This filter instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="patterns"/> is null.
    /// </exception>
    public ToolNamePatternFilter DenyMany(IEnumerable<string> patterns, RegexOptions options = RegexOptions.IgnoreCase)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        foreach (var pattern in patterns)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                Deny(pattern, options);
            }
        }

        return this;
    }

    /// <inheritdoc/>
    public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(context);

        bool isAllowed = EvaluateAccess(tool.Name);
        return Task.FromResult(isAllowed);
    }

    /// <inheritdoc/>
    public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        ArgumentNullException.ThrowIfNull(context);

        bool isAllowed = EvaluateAccess(toolName);
        
        return Task.FromResult(isAllowed
            ? AuthorizationResult.Allow($"Tool '{toolName}' matches allowed patterns")
            : AuthorizationResult.Deny($"Tool '{toolName}' is not allowed by pattern filter"));
    }

    /// <summary>
    /// Evaluates whether access should be granted for the specified tool name.
    /// </summary>
    /// <param name="toolName">The tool name to evaluate.</param>
    /// <returns><see langword="true"/> if access should be granted; otherwise, <see langword="false"/>.</returns>
    private bool EvaluateAccess(string toolName)
    {
        // Check deny patterns first (they take precedence)
        foreach (var denyPattern in _denyPatterns)
        {
            if (denyPattern.IsMatch(toolName))
            {
                return false;
            }
        }

        // Check allow patterns
        foreach (var allowPattern in _allowPatterns)
        {
            if (allowPattern.IsMatch(toolName))
            {
                return true;
            }
        }

        // No patterns matched, return default behavior
        return _defaultAllow;
    }

    /// <summary>
    /// Creates a filter that allows only tools matching the specified patterns.
    /// </summary>
    /// <param name="allowPatterns">Patterns that allow tool access.</param>
    /// <param name="priority">The priority for this filter.</param>
    /// <returns>A new <see cref="ToolNamePatternFilter"/> instance.</returns>
    public static ToolNamePatternFilter CreateAllowList(IEnumerable<string> allowPatterns, int priority = 100)
    {
        var filter = new ToolNamePatternFilter(priority, defaultAllow: false);
        filter.AllowMany(allowPatterns);
        return filter;
    }

    /// <summary>
    /// Creates a filter that denies only tools matching the specified patterns.
    /// </summary>
    /// <param name="denyPatterns">Patterns that deny tool access.</param>
    /// <param name="priority">The priority for this filter.</param>
    /// <returns>A new <see cref="ToolNamePatternFilter"/> instance.</returns>
    public static ToolNamePatternFilter CreateDenyList(IEnumerable<string> denyPatterns, int priority = 100)
    {
        var filter = new ToolNamePatternFilter(priority, defaultAllow: true);
        filter.DenyMany(denyPatterns);
        return filter;
    }
}