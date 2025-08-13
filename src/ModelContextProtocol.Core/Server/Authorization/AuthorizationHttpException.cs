using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server.Authorization;

/// <summary>
/// Represents an authorization exception that requires an HTTP challenge response.
/// </summary>
/// <remarks>
/// This exception is used to indicate that tool authorization has failed and 
/// the client should receive a proper HTTP challenge response with WWW-Authenticate headers.
/// </remarks>
public sealed class AuthorizationHttpException : McpException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationHttpException"/> class.
    /// </summary>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="wwwAuthenticateHeaderValue">The WWW-Authenticate header value to include in the HTTP response.</param>
    /// <param name="httpStatusCode">The HTTP status code to return (default: 401 Unauthorized).</param>
    public AuthorizationHttpException(
        string toolName, 
        string reason, 
        string? wwwAuthenticateHeaderValue = null,
        int httpStatusCode = 401)
        : base($"Access denied for tool '{toolName}': {reason}", McpErrorCode.InvalidParams)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        WwwAuthenticateHeaderValue = wwwAuthenticateHeaderValue;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthorizationHttpException"/> class.
    /// </summary>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="innerException">The inner exception that caused the authorization failure.</param>
    /// <param name="wwwAuthenticateHeaderValue">The WWW-Authenticate header value to include in the HTTP response.</param>
    /// <param name="httpStatusCode">The HTTP status code to return (default: 401 Unauthorized).</param>
    public AuthorizationHttpException(
        string toolName, 
        string reason, 
        Exception innerException,
        string? wwwAuthenticateHeaderValue = null,
        int httpStatusCode = 401)
        : base($"Access denied for tool '{toolName}': {reason}", McpErrorCode.InvalidParams, innerException)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        WwwAuthenticateHeaderValue = wwwAuthenticateHeaderValue;
        HttpStatusCode = httpStatusCode;
    }

    /// <summary>
    /// Gets the name of the tool that was denied access.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the reason for the authorization failure.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the WWW-Authenticate header value to include in the HTTP response.
    /// </summary>
    /// <remarks>
    /// If this is null, no WWW-Authenticate header will be added to the response.
    /// </remarks>
    public string? WwwAuthenticateHeaderValue { get; }

    /// <summary>
    /// Gets the HTTP status code to return in the response.
    /// </summary>
    /// <value>
    /// The HTTP status code, typically 401 (Unauthorized) or 403 (Forbidden).
    /// </value>
    public int HttpStatusCode { get; }

    /// <summary>
    /// Creates an <see cref="AuthorizationHttpException"/> for OAuth2 Bearer token authentication.
    /// </summary>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="scope">Optional scope parameter for the WWW-Authenticate header.</param>
    /// <param name="error">Optional error parameter for the WWW-Authenticate header (e.g., "insufficient_scope").</param>
    /// <param name="errorDescription">Optional error_description parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationHttpException"/> configured for OAuth2 Bearer token authentication.</returns>
    public static AuthorizationHttpException CreateBearerChallenge(
        string toolName,
        string reason,
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

        return new AuthorizationHttpException(toolName, reason, wwwAuthenticate);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationHttpException"/> for Basic authentication.
    /// </summary>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="realm">The realm parameter for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationHttpException"/> configured for Basic authentication.</returns>
    public static AuthorizationHttpException CreateBasicChallenge(
        string toolName,
        string reason,
        string? realm = null)
    {
        var wwwAuthenticate = !string.IsNullOrEmpty(realm) 
            ? $"Basic realm=\"{realm}\""
            : "Basic";

        return new AuthorizationHttpException(toolName, reason, wwwAuthenticate);
    }

    /// <summary>
    /// Creates an <see cref="AuthorizationHttpException"/> for a custom authentication scheme.
    /// </summary>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="scheme">The authentication scheme name (e.g., "Custom", "ApiKey").</param>
    /// <param name="parameters">Optional parameters for the WWW-Authenticate header.</param>
    /// <returns>An <see cref="AuthorizationHttpException"/> configured for the custom authentication scheme.</returns>
    public static AuthorizationHttpException CreateCustomChallenge(
        string toolName,
        string reason,
        string scheme,
        params (string name, string value)[] parameters)
    {
        var challengeParts = parameters?.Select(p => $"{p.name}=\"{p.value}\"").ToList() ?? new List<string>();
        var wwwAuthenticate = challengeParts.Count > 0 
            ? $"{scheme} {string.Join(", ", challengeParts)}"
            : scheme;

        return new AuthorizationHttpException(toolName, reason, wwwAuthenticate);
    }
}