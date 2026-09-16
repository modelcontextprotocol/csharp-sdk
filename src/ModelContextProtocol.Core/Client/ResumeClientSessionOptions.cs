using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Provides the metadata captured from a previous MCP client session that is required to resume it.
/// </summary>
public sealed class ResumeClientSessionOptions
{
    /// <summary>
    /// Gets or sets the server capabilities that were negotiated during the original session initialization.
    /// </summary>
    public required ServerCapabilities ServerCapabilities { get; set; }

    /// <summary>
    /// Gets or sets the server implementation metadata that identifies the connected MCP server.
    /// </summary>
    public required Implementation ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets any instructions previously supplied by the server.
    /// </summary>
    public string? ServerInstructions { get; set; }

    /// <summary>
    /// Gets or sets the protocol version that was negotiated with the server.
    /// </summary>
    public string? NegotiatedProtocolVersion { get; set; }
}
