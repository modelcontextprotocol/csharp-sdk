namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Represents an HTTP authorization challenge that should be sent to the client.
/// </summary>
/// <remarks>
/// This class provides structured information for HTTP authorization challenges,
/// including the WWW-Authenticate header value and HTTP status code to return.
/// </remarks>
public sealed class AuthorizationChallenge
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationChallenge"/> class.
    /// </summary>
    /// <param name="wwwAuthenticateValue">The WWW-Authenticate header value.</param>
    /// <param name="httpStatusCode">The HTTP status code to return (default: 401 Unauthorized).</param>
    public AuthorizationChallenge(string wwwAuthenticateValue, int httpStatusCode = 401)
    {
        WwwAuthenticateValue = wwwAuthenticateValue ?? throw new ArgumentNullException(nameof(wwwAuthenticateValue));
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Gets the WWW-Authenticate header value to include in the HTTP response.
    /// </summary>
    public string WwwAuthenticateValue { get; }

    /// <summary>
    /// Gets the HTTP status code to return in the response.
    /// </summary>
    /// <value>
    /// The HTTP status code, typically 401 (Unauthorized) or 403 (Forbidden).
    /// </value>
    public int HttpStatusCode { get; }

    /// <summary>
    /// Creates an <see cref="AuthorizationChallenge"/> for OAuth2 Bearer token authentication.
    /// </summary>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="scope">Optional scope parameter for the WWW-Authenticate header.</param>
    /// <param name="error">Optional error parameter for the WWW-Authenticate header (e.g., "insufficient_scope").</param>
    /// <param name="errorDescription">Optional error_description parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationChallenge"/> configured for OAuth2 Bearer token authentication.</returns>
    public static AuthorizationChallenge CreateBearerChallenge(
        string? realm = null,
        string? scope = null,
        string? error = null,
        string? errorDescription = null)
    {
        var challengeParts = new List<string>();
        
        if (!string.IsNullOrEmpty(realm))
            challengeParts.Add($"realm=\"{realm}\"");
        
        if (!string.IsNullOrEmpty(scope))
            challengeParts.Add($"scope=\"{scope}\"");
        
        if (!string.IsNullOrEmpty(error))
            challengeParts.Add($"error=\"{error}\"");
        
        if (!string.IsNullOrEmpty(errorDescription))
            challengeParts.Add($"error_description=\"{errorDescription}\"");

        var wwwAuthenticate = challengeParts.Count > 0 
            ? $"Bearer {string.Join(", ", challengeParts)}"
            : "Bearer";

        return new AuthorizationChallenge(wwwAuthenticate);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationChallenge"/> for Basic authentication.
    /// </summary>
    /// <param name="realm">The realm parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationChallenge"/> configured for Basic authentication.</returns>
    public static AuthorizationChallenge CreateBasicChallenge(string? realm = null)
    {
        var wwwAuthenticate = !string.IsNullOrEmpty(realm) 
            ? $"Basic realm=\"{realm}\""
            : "Basic";

        return new AuthorizationChallenge(wwwAuthenticate);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationChallenge"/> for a custom authentication scheme.
    /// </summary>
    /// <param name="scheme">The authentication scheme name (e.g., "Custom", "ApiKey").</param>
    /// <param name="parameters">Optional parameters for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationChallenge"/> configured for the custom authentication scheme.</returns>
    public static AuthorizationChallenge CreateCustomChallenge(
        string scheme,
        params (string name, string value)[] parameters)
    {
        var challengeParts = parameters?.Select(p => $"{p.name}=\"{p.value}\"").ToList() ?? new List<string>();
        var wwwAuthenticate = challengeParts.Count > 0 
            ? $"{scheme} {string.Join(", ", challengeParts)}"
            : scheme;

        return new AuthorizationChallenge(wwwAuthenticate);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationChallenge"/> for OAuth2 insufficient scope error.
    /// </summary>
    /// <param name="requiredScope">The scope required to access the resource.</param>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationChallenge"/> configured for insufficient scope error.</returns>
    public static AuthorizationChallenge CreateInsufficientScopeChallenge(
        string requiredScope,
        string? realm = null)
    {
        return CreateBearerChallenge(
            realm: realm,
            scope: requiredScope,
            error: "insufficient_scope",
            errorDescription: $"The request requires higher privileges than provided by the access token. Required scope: {requiredScope}");
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationChallenge"/> for OAuth2 invalid token error.
    /// </summary>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="errorDescription">Optional custom error description.</param>
    /// <returns>An <see cref="AuthorizationChallenge"/> configured for invalid token error.</returns>
    public static AuthorizationChallenge CreateInvalidTokenChallenge(
        string? realm = null,
        string? errorDescription = null)
    {
        return CreateBearerChallenge(
            realm: realm,
            error: "invalid_token",
            errorDescription: errorDescription ?? "The access token provided is expired, revoked, malformed, or invalid for other reasons");
    }
}