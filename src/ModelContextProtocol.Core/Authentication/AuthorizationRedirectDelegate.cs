
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that handles the OAuth authorization URL and returns the authorization code.
/// </summary>
/// <param name="authorizationUri">The authorization URL that the user needs to visit.</param>
/// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
/// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
/// <returns>A task that represents the asynchronous operation. The task result contains the authorization code if successful, or null if the operation failed or was cancelled.</returns>
/// <remarks>
/// <para>
/// This delegate provides SDK consumers with full control over how the OAuth authorization flow is handled.
/// Implementers can choose to:
/// </para>
/// <list type="bullet">
/// <item><description>Start a local HTTP server and open a browser (default behavior)</description></item>
/// <item><description>Display the authorization URL to the user for manual handling</description></item>
/// <item><description>Integrate with a custom UI or authentication flow</description></item>
/// <item><description>Use a different redirect mechanism altogether</description></item>
/// </list>
/// <para>
/// The implementation should handle user interaction to visit the authorization URL and extract
/// the authorization code from the callback. The authorization code is typically provided as
/// a query parameter in the redirect URI callback.
/// </para>
/// </remarks>
public delegate Task<string?> AuthorizationRedirectDelegate(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken);