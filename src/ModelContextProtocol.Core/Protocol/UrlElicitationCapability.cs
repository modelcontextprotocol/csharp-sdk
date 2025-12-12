namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for URL mode (out-of-band) elicitation.
/// </summary>
/// <remarks>
/// <para>
/// This capability enables secure out-of-band interactions where the user is directed to a URL
/// (typically opened in a browser) to complete sensitive operations like OAuth authorization,
/// payments, or credential entry.
/// </para>
/// <para>
/// Unlike form mode, sensitive data in URL mode is never exposed to the MCP client, providing
/// better security for sensitive interactions.
/// </para>
/// </remarks>
public sealed class UrlElicitationCapability;
