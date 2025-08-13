namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Represents the result of an authorization check for tool operations.
/// </summary>
/// <remarks>
/// This class encapsulates the outcome of authorization decisions, providing
/// both the boolean result and optional additional context for failed authorizations.
/// </remarks>
public sealed class AuthorizationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationResult"/> class.
    /// </summary>
    /// <param name="isAuthorized">Indicates whether the operation is authorized.</param>
    /// <param name="reason">Optional reason for the authorization decision, particularly useful for denied operations.</param>
    /// <param name="additionalData">Optional additional data that may be relevant to the authorization decision.</param>
    public AuthorizationResult(bool isAuthorized, string? reason = null, object? additionalData = null)
    {
        IsAuthorized = isAuthorized;
        Reason = reason;
        AdditionalData = additionalData;
    }

    /// <summary>
    /// Gets a value indicating whether the operation is authorized.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the operation is authorized; otherwise, <see langword="false"/>.
    /// </value>
    public bool IsAuthorized { get; }

    /// <summary>
    /// Gets the reason for the authorization decision.
    /// </summary>
    /// <value>
    /// A string describing the reason for the authorization result, or <see langword="null"/>
    /// if no specific reason was provided.
    /// </value>
    /// <remarks>
    /// This property is particularly useful for denied operations where clients or
    /// administrators need to understand why access was refused.
    /// </remarks>
    public string? Reason { get; }

    /// <summary>
    /// Gets additional data associated with the authorization decision.
    /// </summary>
    /// <value>
    /// An object containing additional context data, or <see langword="null"/>
    /// if no additional data is available.
    /// </value>
    /// <remarks>
    /// This property can be used to pass implementation-specific data that may be
    /// useful for logging, auditing, or other authorization-related processes.
    /// </remarks>
    public object? AdditionalData { get; }

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a successful authorization.
    /// </summary>
    /// <param name="reason">Optional reason for the successful authorization.</param>
    /// <param name="additionalData">Optional additional data related to the authorization.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="true"/>.</returns>
    public static AuthorizationResult Allow(string? reason = null, object? additionalData = null)
        => new(true, reason, additionalData);

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization.
    /// </summary>
    /// <param name="reason">Reason for the authorization denial.</param>
    /// <param name="additionalData">Optional additional data related to the authorization failure.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult Deny(string reason, object? additionalData = null)
        => new(false, reason, additionalData);

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization with a default reason.
    /// </summary>
    /// <param name="additionalData">Optional additional data related to the authorization failure.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult Deny(object? additionalData = null)
        => new(false, "Access denied", additionalData);

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization with an HTTP challenge.
    /// </summary>
    /// <param name="reason">Reason for the authorization denial.</param>
    /// <param name="challenge">The authorization challenge to include in the HTTP response.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult DenyWithChallenge(string reason, AuthorizationChallenge challenge)
        => new(false, reason, challenge);

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization with a Bearer token challenge.
    /// </summary>
    /// <param name="reason">Reason for the authorization denial.</param>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="scope">Optional scope parameter for the WWW-Authenticate header.</param>
    /// <param name="error">Optional error parameter for the WWW-Authenticate header (e.g., "insufficient_scope").</param>
    /// <param name="errorDescription">Optional error_description parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult DenyWithBearerChallenge(
        string reason,
        string? realm = null,
        string? scope = null,
        string? error = null,
        string? errorDescription = null)
    {
        var challenge = AuthorizationChallenge.CreateBearerChallenge(realm, scope, error, errorDescription);
        return new(false, reason, challenge);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization with a Basic authentication challenge.
    /// </summary>
    /// <param name="reason">Reason for the authorization denial.</param>
    /// <param name="realm">The realm parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult DenyWithBasicChallenge(string reason, string? realm = null)
    {
        var challenge = AuthorizationChallenge.CreateBasicChallenge(realm);
        return new(false, reason, challenge);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization due to insufficient scope.
    /// </summary>
    /// <param name="requiredScope">The scope required to access the resource.</param>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult DenyInsufficientScope(string requiredScope, string? realm = null)
    {
        var challenge = AuthorizationChallenge.CreateInsufficientScopeChallenge(requiredScope, realm);
        return new(false, $"Insufficient scope. Required scope: {requiredScope}", challenge);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationResult"/> representing a denied authorization due to invalid token.
    /// </summary>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="errorDescription">Optional custom error description.</param>
    /// <returns>An <see cref="AuthorizationResult"/> with <see cref="IsAuthorized"/> set to <see langword="false"/>.</returns>
    public static AuthorizationResult DenyInvalidToken(string? realm = null, string? errorDescription = null)
    {
        var challenge = AuthorizationChallenge.CreateInvalidTokenChallenge(realm, errorDescription);
        return new(false, "Invalid or expired token", challenge);
    }

    /// <summary>
    /// Returns a string representation of the authorization result.
    /// </summary>
    /// <returns>A string describing the authorization result.</returns>
    public override string ToString()
    {
        return IsAuthorized switch
        {
            true when !string.IsNullOrEmpty(Reason) => $"Authorized: {Reason}",
            true => "Authorized",
            false when !string.IsNullOrEmpty(Reason) => $"Denied: {Reason}",
            false => "Denied"
        };
    }
}