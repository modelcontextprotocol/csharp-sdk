namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that handles the OAuth authorization URL and returns the authorization code.
/// </summary>
/// <param name="authorizationUri">The authorization URL that the user needs to visit.</param>
/// <param name="redirectUri">The redirect URI where the authorization code will be sent.</param>
/// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
/// <returns>A task that represents the asynchronous operation. The task result contains the authorization code if successful, or null if the operation failed or was cancelled.</returns>
/// <remarks>
/// This delegate cannot return the <c>iss</c> parameter from the authorization response, so
/// <see href="https://datatracker.ietf.org/doc/html/rfc9207">RFC 9207</see> issuer validation is
/// skipped when it is used. Use <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/> for
/// issuer-aware authorization flows.
/// </remarks>
[Obsolete(
    ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_Message,
    DiagnosticId = ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_DiagnosticId,
    UrlFormat = ModelContextProtocol.Obsoletions.AuthorizationRedirectDelegate_Url)]
public delegate Task<string?> AuthorizationRedirectDelegate(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken);
