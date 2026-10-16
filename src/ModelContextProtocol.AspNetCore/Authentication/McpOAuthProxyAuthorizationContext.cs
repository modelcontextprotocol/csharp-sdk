using Microsoft.AspNetCore.Http;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Describes a validated proxy authorization request for application policy evaluation.
/// </summary>
public sealed class McpOAuthProxyAuthorizationContext
{
    /// <summary>
    /// Gets the HTTP context for the authorization request.
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Gets the registered dynamic client identifier.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets the registered client name, when present.
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// Gets the exact registered redirect URI selected by the client.
    /// </summary>
    public required string RedirectUri { get; init; }

    /// <summary>
    /// Gets the requested scopes.
    /// </summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>
    /// Gets the validated resource indicator, when present.
    /// </summary>
    public string? Resource { get; init; }
}
