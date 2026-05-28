using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result returned from a <see cref="RequestMethods.ServerDiscover"/> request.
/// </summary>
/// <remarks>
/// <para>
/// Introduced by the draft protocol revision (SEP-2575) as the canonical way for a client
/// to learn what a server supports without performing the legacy <c>initialize</c> handshake.
/// </para>
/// </remarks>
public sealed class DiscoverResult : Result
{
    /// <summary>
    /// Gets or sets the list of MCP protocol version strings that the server supports.
    /// </summary>
    /// <remarks>
    /// The client should choose a version from this list for use in subsequent requests.
    /// </remarks>
    [JsonPropertyName("supportedVersions")]
    public required IList<string> SupportedVersions { get; set; }

    /// <summary>
    /// Gets or sets the capabilities of the server.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public required ServerCapabilities Capabilities { get; set; }

    /// <summary>
    /// Gets or sets information about the server implementation.
    /// </summary>
    [JsonPropertyName("serverInfo")]
    public required Implementation ServerInfo { get; set; }

    /// <summary>
    /// Gets or sets optional instructions describing how to use the server and its features.
    /// </summary>
    /// <remarks>
    /// This can be used by clients to improve an LLM's understanding of the server,
    /// for example by including it in a system prompt.
    /// </remarks>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}
