
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that handles the dynamic client registration response.
/// </summary>
/// <param name="response">The dynamic client registration response containing the client credentials.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A task that represents the asynchronous operation.</returns>
/// <remarks>
/// The implementation should save the client credentials securely for future use.
/// </remarks>
public delegate Task DynamicClientRegistrationDelegate(DynamicClientRegistrationResponse response, CancellationToken cancellationToken);