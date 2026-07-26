
namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents a method that selects or filters the OAuth scopes to request during authorization.
/// </summary>
/// <param name="scope">
/// The scopes determined by the MCP scope selection strategy (WWW-Authenticate header scope →
/// <c>scopes_supported</c> from Protected Resource Metadata → <see cref="ClientOAuthOptions.Scopes"/>
/// fallback), with <c>offline_access</c> appended when advertised by the authorization server. May be
/// <see langword="null"/> if the server provided no scope information and no fallback scopes are configured.
/// </param>
/// <returns>
/// The scopes to include in the authorization and Dynamic Client Registration requests. Return
/// <see langword="null"/> or an empty enumerable to omit the <c>scope</c> parameter entirely.
/// </returns>
/// <remarks>
/// <para>
/// Use this delegate to filter or customize the proposed scopes before the authorization request is made.
/// Common scenarios include:
/// </para>
/// <list type="bullet">
/// <item><description>Requesting only a subset of the scopes offered by the server.</description></item>
/// <item><description>Appending a custom scope not advertised in the server metadata.</description></item>
/// </list>
/// <para>
/// The MCP specification defines the following scope selection priority (highest to lowest):
/// WWW-Authenticate header scope → PRM <c>scopes_supported</c> → omit scope parameter. The
/// <paramref name="scope"/> parameter already reflects this priority. The delegate runs after
/// <c>offline_access</c> has been auto-appended, so it can also remove that scope if desired.
/// </para>
/// <para>
/// The resolved scope is applied consistently to both the authorization URL and the Dynamic Client
/// Registration (DCR) request, so the registered client scope matches what is actually requested.
/// </para>
/// </remarks>
public delegate IEnumerable<string>? ScopeSelectorDelegate(IReadOnlyCollection<string>? scope);
