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
    /// Gets or sets the client secret.
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Gets or sets the list of redirect URIs allowed for this client.
    /// </summary>
    public List<string> RedirectUris { get; init; } = [];
}