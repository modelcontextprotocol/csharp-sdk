using ModelContextProtocol.Types.Authentication;

namespace ModelContextProtocol.Authentication;

/// <summary>
/// Defines an interface for registering OAuth clients with authorization servers.
/// This is an extensibility point for client registration in MCP clients.
/// </summary>
public interface IClientRegistrationProvider
{
    /// <summary>
    /// Registers a client with an OAuth authorization server.
    /// </summary>
    /// <param name="authorizationServerMetadata">Metadata about the authorization server.</param>
    /// <param name="registrationRequest">The client registration request data.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The client registration response containing client credentials.</returns>
    Task<ClientRegistrationResponse?> RegisterClientAsync(
        AuthorizationServerMetadata authorizationServerMetadata,
        ClientRegistrationRequest registrationRequest,
        CancellationToken cancellationToken = default);
}