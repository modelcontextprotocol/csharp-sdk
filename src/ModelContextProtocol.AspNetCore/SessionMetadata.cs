namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Contains the persistable metadata for an MCP session.
/// </summary>
/// <remarks>
/// This class contains all the information needed to recreate an MCP session
/// after a server restart or when routing to a different server instance.
/// Only serializable data is included - runtime objects like McpServer and
/// transport connections cannot be persisted and must be recreated.
/// </remarks>
public sealed class SessionMetadata
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier claim type (e.g., "sub", "nameidentifier").
    /// </summary>
    public string? UserIdClaimType { get; set; }

    /// <summary>
    /// Gets or sets the user identifier claim value.
    /// </summary>
    public string? UserIdClaimValue { get; set; }

    /// <summary>
    /// Gets or sets the user identifier claim issuer.
    /// </summary>
    public string? UserIdClaimIssuer { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last activity.
    /// </summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>
    /// Gets or sets optional JSON-serialized custom data that tools or middleware
    /// may want to persist across session recreations.
    /// </summary>
    /// <remarks>
    /// The implementor of <see cref="ISessionStore"/> is responsible for serializing
    /// and deserializing this data as needed.
    /// </remarks>
    public string? CustomDataJson { get; set; }
}
