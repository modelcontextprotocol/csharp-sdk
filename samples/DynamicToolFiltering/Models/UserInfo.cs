namespace DynamicToolFiltering.Models;

/// <summary>
/// Represents user information for the filtering system.
/// </summary>
public class UserInfo
{
    /// <summary>
    /// Gets or sets the unique user identifier.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's primary role.
    /// </summary>
    public string Role { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the list of scopes assigned to the user.
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Gets or sets the tenant ID associated with the user.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets whether the user is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the user last authenticated.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets custom user properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = new();
}