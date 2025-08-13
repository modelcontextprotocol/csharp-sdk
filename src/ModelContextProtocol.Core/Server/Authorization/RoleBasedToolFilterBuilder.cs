using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Builder class for creating role-based tool filters.
/// </summary>
/// <remarks>
/// This builder provides a fluent API for configuring role-based access control
/// for MCP tools, allowing developers to easily specify which roles can access
/// which tools.
/// </remarks>
public sealed class RoleBasedToolFilterBuilder
{
    private readonly Dictionary<string, HashSet<string>> _toolRoles = new();
    private readonly Dictionary<string, HashSet<string>> _roleTools = new();
    private bool _defaultDeny = true;
    private int _priority = 0;

    /// <summary>
    /// Allows a specific role to access a specific tool.
    /// </summary>
    /// <param name="role">The role that should have access.</param>
    /// <param name="toolName">The name of the tool to allow access to.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="role"/> or <paramref name="toolName"/> is null or empty.
    /// </exception>
    public RoleBasedToolFilterBuilder AllowRole(string role, string toolName)
    {
        if (string.IsNullOrEmpty(role))
            throw new ArgumentException("Role cannot be null or empty", nameof(role));
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));

        if (!_toolRoles.ContainsKey(toolName))
        {
            _toolRoles[toolName] = new HashSet<string>();
        }
        _toolRoles[toolName].Add(role);

        if (!_roleTools.ContainsKey(role))
        {
            _roleTools[role] = new HashSet<string>();
        }
        _roleTools[role].Add(toolName);

        return this;
    }

    /// <summary>
    /// Allows a specific role to access multiple tools.
    /// </summary>
    /// <param name="role">The role that should have access.</param>
    /// <param name="toolNames">The names of the tools to allow access to.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="role"/> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="toolNames"/> is null.
    /// </exception>
    public RoleBasedToolFilterBuilder AllowRole(string role, params string[] toolNames)
    {
        if (string.IsNullOrEmpty(role))
            throw new ArgumentException("Role cannot be null or empty", nameof(role));
        
        ArgumentNullException.ThrowIfNull(toolNames);

        foreach (var toolName in toolNames)
        {
            if (!string.IsNullOrEmpty(toolName))
            {
                AllowRole(role, toolName);
            }
        }

        return this;
    }

    /// <summary>
    /// Allows multiple roles to access a specific tool.
    /// </summary>
    /// <param name="toolName">The name of the tool to allow access to.</param>
    /// <param name="roles">The roles that should have access.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="toolName"/> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="roles"/> is null.
    /// </exception>
    public RoleBasedToolFilterBuilder AllowTool(string toolName, params string[] roles)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        
        ArgumentNullException.ThrowIfNull(roles);

        foreach (var role in roles)
        {
            if (!string.IsNullOrEmpty(role))
            {
                AllowRole(role, toolName);
            }
        }

        return this;
    }

    /// <summary>
    /// Sets whether to deny access by default when no explicit rules match.
    /// </summary>
    /// <param name="defaultDeny">
    /// If <see langword="true"/> (default), access is denied when no rules match.
    /// If <see langword="false"/>, access is allowed when no rules match.
    /// </param>
    /// <returns>This builder instance for method chaining.</returns>
    public RoleBasedToolFilterBuilder WithDefaultDeny(bool defaultDeny = true)
    {
        _defaultDeny = defaultDeny;
        return this;
    }

    /// <summary>
    /// Sets the priority for this filter.
    /// </summary>
    /// <param name="priority">
    /// The priority value. Lower values indicate higher priority.
    /// </param>
    /// <returns>This builder instance for method chaining.</returns>
    public RoleBasedToolFilterBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <summary>
    /// Builds the role-based tool filter with the configured rules.
    /// </summary>
    /// <returns>A configured <see cref="IToolFilter"/> implementation.</returns>
    public IToolFilter Build()
    {
        return new RoleBasedToolFilter(_toolRoles, _defaultDeny, _priority);
    }

    /// <summary>
    /// Internal implementation of role-based tool filtering.
    /// </summary>
    private sealed class RoleBasedToolFilter : IToolFilter
    {
        private readonly IReadOnlyDictionary<string, HashSet<string>> _toolRoles;
        private readonly bool _defaultDeny;

        public RoleBasedToolFilter(Dictionary<string, HashSet<string>> toolRoles, bool defaultDeny, int priority)
        {
            _toolRoles = toolRoles.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value,
                StringComparer.OrdinalIgnoreCase);
            _defaultDeny = defaultDeny;
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(tool);
            ArgumentNullException.ThrowIfNull(context);

            return Task.FromResult(IsAuthorized(tool.Name, context));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
            ArgumentNullException.ThrowIfNull(context);

            bool isAuthorized = IsAuthorized(toolName, context);
            
            return Task.FromResult(isAuthorized 
                ? AuthorizationResult.Allow($"Role-based access granted for tool '{toolName}'")
                : AuthorizationResult.Deny($"Role-based access denied for tool '{toolName}' - insufficient permissions"));
        }

        private bool IsAuthorized(string toolName, ToolAuthorizationContext context)
        {
            // If no roles are defined for this user, use default behavior
            if (context.UserRoles.Count == 0)
            {
                return !_defaultDeny;
            }

            // If no rules are defined for this tool, use default behavior
            if (!_toolRoles.TryGetValue(toolName, out var allowedRoles))
            {
                return !_defaultDeny;
            }

            // Check if any of the user's roles are allowed for this tool
            return context.UserRoles.Any(role => allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
        }
    }
}