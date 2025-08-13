using System.Security.Claims;

namespace DynamicToolFiltering.Models;

/// <summary>
/// Represents the execution context for a tool call with relevant filtering information.
/// </summary>
public class ToolExecutionContext
{
    /// <summary>
    /// Gets or sets the name of the tool being executed.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user executing the tool.
    /// </summary>
    public UserInfo? User { get; set; }

    /// <summary>
    /// Gets or sets the claims principal for the current user.
    /// </summary>
    public ClaimsPrincipal? ClaimsPrincipal { get; set; }

    /// <summary>
    /// Gets or sets the session ID for the current session.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Gets or sets the client information.
    /// </summary>
    public string? ClientInfo { get; set; }

    /// <summary>
    /// Gets or sets the IP address of the client.
    /// </summary>
    public string? ClientIpAddress { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the execution was requested.
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the tenant context if applicable.
    /// </summary>
    public TenantContext? TenantContext { get; set; }

    /// <summary>
    /// Gets or sets the execution environment.
    /// </summary>
    public string Environment { get; set; } = "Development";

    /// <summary>
    /// Gets or sets additional context data for filters.
    /// </summary>
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

/// <summary>
/// Represents tenant context information.
/// </summary>
public class TenantContext
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the tenant is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the tenant's subscription tier.
    /// </summary>
    public string SubscriptionTier { get; set; } = "Basic";

    /// <summary>
    /// Gets or sets tenant-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();
}