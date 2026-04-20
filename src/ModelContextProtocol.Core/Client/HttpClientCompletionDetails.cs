using System.Net;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides details about the completion of an HTTP-based MCP client session,
/// including sessions using the legacy SSE transport or the Streamable HTTP transport.
/// </summary>
public sealed class HttpClientCompletionDetails : ClientCompletionDetails
{
    /// <summary>
    /// Gets the HTTP status code that caused the session to close, or <see langword="null"/> if unavailable.
    /// </summary>
    public HttpStatusCode? HttpStatusCode { get; set; }
}
