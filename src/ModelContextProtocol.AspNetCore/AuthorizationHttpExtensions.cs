using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Extension methods for handling authorization challenges in HTTP responses.
/// </summary>
public static class AuthorizationHttpExtensions
{
    /// <summary>
    /// Writes an authorization challenge response to the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context to write the response to.</param>
    /// <param name="authException">The authorization exception containing challenge details.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAuthorizationChallengeAsync(
        this HttpContext context,
        AuthorizationHttpException authException,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authException);

        // Set the HTTP status code
        context.Response.StatusCode = authException.HttpStatusCode;

        // Add WWW-Authenticate header if provided
        if (!string.IsNullOrEmpty(authException.WwwAuthenticateHeaderValue))
        {
            context.Response.Headers.WWWAuthenticate = authException.WwwAuthenticateHeaderValue;
        }

        // Create JSON-RPC error response
        var jsonRpcError = new JsonRpcError
        {
            Error = new JsonRpcErrorDetail
            {
                Code = (int)authException.ErrorCode,
                Message = authException.Message,
                Data = new
                {
                    ToolName = authException.ToolName,
                    Reason = authException.Reason,
                    HttpStatusCode = authException.HttpStatusCode,
                    RequiresAuthentication = !string.IsNullOrEmpty(authException.WwwAuthenticateHeaderValue)
                }
            }
        };

        // Set content type and write the JSON response
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body, 
            jsonRpcError, 
            McpJsonUtilities.JsonContext.Default.JsonRpcError,
            cancellationToken);
    }

    /// <summary>
    /// Writes a generic authorization error response to the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context to write the response to.</param>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="statusCode">The HTTP status code to return (default: 403 Forbidden).</param>
    /// <param name="wwwAuthenticateValue">Optional WWW-Authenticate header value.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAuthorizationErrorAsync(
        this HttpContext context,
        string toolName,
        string reason,
        int statusCode = StatusCodes.Status403Forbidden,
        string? wwwAuthenticateValue = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        var authException = new AuthorizationHttpException(toolName, reason, wwwAuthenticateValue, statusCode);
        await context.WriteAuthorizationChallengeAsync(authException, cancellationToken);
    }

    /// <summary>
    /// Creates a Bearer token challenge response for OAuth2 authentication.
    /// </summary>
    /// <param name="context">The HTTP context to write the response to.</param>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="realm">Optional realm parameter for the WWW-Authenticate header.</param>
    /// <param name="scope">Optional scope parameter for the WWW-Authenticate header.</param>
    /// <param name="error">Optional error parameter for the WWW-Authenticate header (e.g., "insufficient_scope").</param>
    /// <param name="errorDescription">Optional error_description parameter for the WWW-Authenticate header.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteBearerChallengeAsync(
        this HttpContext context,
        string toolName,
        string reason,
        string? realm = null,
        string? scope = null,
        string? error = null,
        string? errorDescription = null,
        CancellationToken cancellationToken = default)
    {
        var authException = AuthorizationHttpException.CreateBearerChallenge(
            toolName, reason, realm, scope, error, errorDescription);
        await context.WriteAuthorizationChallengeAsync(authException, cancellationToken);
    }

    /// <summary>
    /// Creates a Basic authentication challenge response.
    /// </summary>
    /// <param name="context">The HTTP context to write the response to.</param>
    /// <param name="toolName">The name of the tool that was denied access.</param>
    /// <param name="reason">The reason for the authorization failure.</param>
    /// <param name="realm">The realm parameter for the WWW-Authenticate header.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteBasicChallengeAsync(
        this HttpContext context,
        string toolName,
        string reason,
        string? realm = null,
        CancellationToken cancellationToken = default)
    {
        var authException = AuthorizationHttpException.CreateBasicChallenge(toolName, reason, realm);
        await context.WriteAuthorizationChallengeAsync(authException, cancellationToken);
    }

    /// <summary>
    /// Determines if an exception should result in an HTTP authorization challenge.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns>True if the exception should result in an authorization challenge, false otherwise.</returns>
    public static bool ShouldChallengeAuthorization(this Exception exception)
    {
        return exception is AuthorizationHttpException ||
               (exception is McpException mcpEx && mcpEx.ErrorCode == McpErrorCode.InvalidParams &&
                mcpEx.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tries to extract tool name from an authorization-related exception message.
    /// </summary>
    /// <param name="exception">The exception to extract the tool name from.</param>
    /// <returns>The tool name if found, otherwise null.</returns>
    public static string? TryExtractToolName(this Exception exception)
    {
        if (exception is AuthorizationHttpException authEx)
        {
            return authEx.ToolName;
        }

        if (exception?.Message is string message)
        {
            // Try to extract tool name from messages like "Access denied for tool 'toolName'"
            var startIndex = message.IndexOf("tool '", StringComparison.OrdinalIgnoreCase);
            if (startIndex >= 0)
            {
                startIndex += 6; // Length of "tool '"
                var endIndex = message.IndexOf('\'', startIndex);
                if (endIndex > startIndex)
                {
                    return message.Substring(startIndex, endIndex - startIndex);
                }
            }
        }

        return null;
    }
}