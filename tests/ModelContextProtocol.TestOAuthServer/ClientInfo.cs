using System.Text.Json.Serialization;

namespace ModelContextProtocol.TestOAuthServer;

/// <summary>
/// Represents client information for OAuth flow.
/// </summary>
internal sealed class ClientInfo
{
    /// <summary>
    /// Gets or sets the client ID.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Gets or sets whether a client secret is required.
    /// </summary>
    public required bool RequiresClientSecret { get; init; }

    /// <summary>
    /// Gets or sets the client secret.
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Gets or sets the list of redirect URIs allowed for this client.
    /// </summary>
    public List<string> RedirectUris { get; init; } = [];

    /// <summary>
    /// Gets or sets the token endpoint auth method for this client.
    /// Supported values: "client_secret_post", "client_secret_basic", "private_key_jwt", "none"
    /// </summary>
    public string TokenEndpointAuthMethod { get; init; } = "client_secret_post";

    /// <summary>
    /// Gets or sets the allowed grant types for this client.
    /// </summary>
    public List<string> AllowedGrantTypes { get; init; } = ["authorization_code", "refresh_token"];

    /// <summary>
    /// Gets or sets the client's JWKS URI for JWT client assertion verification.
    /// </summary>
    public string? JwksUri { get; init; }

    /// <summary>
    /// Gets or sets the client's public key PEM for JWT client assertion verification (inline, no JWKS fetch).
    /// </summary>
    public string? PublicKeyPem { get; init; }
}