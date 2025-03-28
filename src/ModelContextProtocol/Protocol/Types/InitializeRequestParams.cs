using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Parameters for an initialization request sent to the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class InitializeRequestParams : RequestParams
{
    /// <summary>
    /// The version of the Model Context Protocol that the client wants to use.
    /// </summary>
    [JsonPropertyName("protocolVersion")]

    public required string ProtocolVersion { get; init; }
    /// <summary>
    /// The client's capabilities.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public ClientCapabilities? Capabilities { get; init; }

    /// <summary>
    /// Information about the client implementation.
    /// </summary>
    [JsonPropertyName("clientInfo")]
    public required Implementation ClientInfo { get; init; }
}
