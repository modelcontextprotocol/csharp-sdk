using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Provides context information for tool authorization operations.
/// </summary>
/// <remarks>
/// This class contains all the contextual information needed by tool filters
/// to make authorization decisions, including user identity, session information,
/// and request metadata.
/// </remarks>
public sealed class ToolAuthorizationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolAuthorizationContext"/> class.
    /// </summary>
    /// <param name="sessionId">The unique identifier for the current session.</param>
    /// <param name="clientInfo">Information about the client making the request.</param>
    /// <param name="serverCapabilities">The capabilities supported by the server.</param>
    public ToolAuthorizationContext(
        string? sessionId,
        Implementation? clientInfo,
        ServerCapabilities? serverCapabilities)
    {
        SessionId = sessionId;
        ClientInfo = clientInfo;
        ServerCapabilities = serverCapabilities;
        Properties = new Dictionary<string, object?>();
    }

    /// <summary>
    /// Gets the unique identifier for the current session.
    /// </summary>
    /// <value>
    /// A string representing the session identifier, or <see langword="null"/>
    /// if no session identifier is available.
    /// </value>
    /// <remarks>
    /// The session ID can be used to track and correlate authorization decisions
    /// across multiple requests within the same session.
    /// </remarks>
    public string? SessionId { get; }

    /// <summary>
    /// Gets information about the client making the request.
    /// </summary>
    /// <value>
    /// An <see cref="Implementation"/> object containing client details,
    /// or <see langword="null"/> if client information is not available.
    /// </value>
    /// <remarks>
    /// Client information includes details such as the client name, version,
    /// and other metadata that may be relevant for authorization decisions.
    /// </remarks>
    public Implementation? ClientInfo { get; }

    /// <summary>
    /// Gets the capabilities supported by the server.
    /// </summary>
    /// <value>
    /// A <see cref="ServerCapabilities"/> object describing server capabilities,
    /// or <see langword="null"/> if capability information is not available.
    /// </value>
    /// <remarks>
    /// Server capabilities can be used by authorization filters to make decisions
    /// based on what features and operations the server supports.
    /// </remarks>
    public ServerCapabilities? ServerCapabilities { get; }

    /// <summary>
    /// Gets or sets the user identifier for the current request.
    /// </summary>
    /// <value>
    /// A string representing the user identifier, or <see langword="null"/>
    /// if no user identifier is available.
    /// </value>
    /// <remarks>
    /// The user ID is typically set by authentication middleware or during
    /// the session initialization process. It can be used by authorization
    /// filters to make user-specific access control decisions.
    /// </remarks>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the user roles for the current request.
    /// </summary>
    /// <value>
    /// A collection of strings representing user roles, or an empty collection
    /// if no roles are assigned.
    /// </value>
    /// <remarks>
    /// User roles can be used by authorization filters to implement role-based
    /// access control (RBAC) for tool operations.
    /// </remarks>
    public ICollection<string> UserRoles { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the user permissions for the current request.
    /// </summary>
    /// <value>
    /// A collection of strings representing user permissions, or an empty collection
    /// if no permissions are assigned.
    /// </value>
    /// <remarks>
    /// User permissions provide fine-grained access control capabilities beyond
    /// role-based access control, allowing for specific operation-level authorization.
    /// </remarks>
    public ICollection<string> UserPermissions { get; set; } = new List<string>();

    /// <summary>
    /// Gets a dictionary of additional properties that can be used to store custom context data.
    /// </summary>
    /// <value>
    /// A dictionary containing key-value pairs of additional context data.
    /// </value>
    /// <remarks>
    /// This property allows for extensibility by enabling custom authorization
    /// filters to store and retrieve implementation-specific context data.
    /// </remarks>
    public IDictionary<string, object?> Properties { get; }

    /// <summary>
    /// Creates a new <see cref="ToolAuthorizationContext"/> with basic session information.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>A new <see cref="ToolAuthorizationContext"/> instance.</returns>
    public static ToolAuthorizationContext ForSession(string? sessionId)
        => new(sessionId, null, null);

    /// <summary>
    /// Creates a new <see cref="ToolAuthorizationContext"/> with session and client information.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="clientInfo">Information about the client.</param>
    /// <returns>A new <see cref="ToolAuthorizationContext"/> instance.</returns>
    public static ToolAuthorizationContext ForSessionAndClient(string? sessionId, Implementation? clientInfo)
        => new(sessionId, clientInfo, null);

    /// <summary>
    /// Creates a copy of this context with additional user information.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roles">Optional user roles.</param>
    /// <param name="permissions">Optional user permissions.</param>
    /// <returns>A new <see cref="ToolAuthorizationContext"/> instance with user information.</returns>
    public ToolAuthorizationContext WithUser(string userId, IEnumerable<string>? roles = null, IEnumerable<string>? permissions = null)
    {
        var context = new ToolAuthorizationContext(SessionId, ClientInfo, ServerCapabilities)
        {
            UserId = userId,
            UserRoles = roles?.ToList() ?? new List<string>(),
            UserPermissions = permissions?.ToList() ?? new List<string>()
        };

        // Copy existing properties
        foreach (var property in Properties)
        {
            context.Properties[property.Key] = property.Value;
        }

        return context;
    }

    /// <summary>
    /// Creates a copy of this context with additional properties.
    /// </summary>
    /// <param name="properties">Additional properties to include.</param>
    /// <returns>A new <see cref="ToolAuthorizationContext"/> instance with additional properties.</returns>
    public ToolAuthorizationContext WithProperties(IDictionary<string, object?> properties)
    {
        var context = new ToolAuthorizationContext(SessionId, ClientInfo, ServerCapabilities)
        {
            UserId = UserId,
            UserRoles = UserRoles,
            UserPermissions = UserPermissions
        };

        // Copy existing properties
        foreach (var property in Properties)
        {
            context.Properties[property.Key] = property.Value;
        }

        // Add new properties
        foreach (var property in properties)
        {
            context.Properties[property.Key] = property.Value;
        }

        return context;
    }
}