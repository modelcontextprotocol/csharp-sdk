using Microsoft.AspNetCore.Http;

namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Describes a validated dynamic client registration for application policy evaluation.
/// </summary>
public sealed class McpOAuthProxyClientRegistrationContext
{
    /// <summary>
    /// Gets the HTTP request that submitted the registration.
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Gets the requested redirect URIs.
    /// </summary>
    public required IReadOnlyList<string> RedirectUris { get; init; }

    /// <summary>
    /// Gets the requested scopes.
    /// </summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>
    /// Gets the normalized grant types requested by the dynamic client.
    /// </summary>
    public required IReadOnlyList<string> GrantTypes { get; init; }

    /// <summary>
    /// Gets the optional client name.
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// Gets the optional client information URI.
    /// </summary>
    public string? ClientUri { get; init; }

    /// <summary>
    /// Gets the requested OIDC application type, when present.
    /// </summary>
    public string? ApplicationType { get; init; }
}
