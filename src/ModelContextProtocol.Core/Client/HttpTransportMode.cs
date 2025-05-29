namespace ModelContextProtocol.Client;

/// <summary>
/// Specifies the transport mode for HTTP client connections.
/// </summary>
public enum HttpTransportMode
{
    /// <summary>
    /// Automatically detect the appropriate transport by trying Streamable HTTP first, then falling back to SSE if that fails.
    /// This is the recommended mode for maximum compatibility.
    /// </summary>
    AutoDetect,

    /// <summary>
    /// Use only the Streamable HTTP transport.
    /// </summary>
    StreamableHttp,

    /// <summary>
    /// Use only the HTTP with SSE transport.
    /// </summary>
    Sse
}