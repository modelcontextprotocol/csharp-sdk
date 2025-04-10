using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Specifies the context inclusion options for a request in the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
[JsonConverter(typeof(CustomizableJsonStringEnumConverter<ContextInclusion>))]
public enum ContextInclusion
{
    /// <summary>
    /// Indicates that no context should be included.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// Indicates that context from the server that sent the request should be included.
    /// </summary>
    [JsonStringEnumMemberName("thisServer")]
    ThisServer,

    /// <summary>
    /// Indicates that context from all servers that the client is connected to should be included.
    /// </summary>
    [JsonStringEnumMemberName("allServers")]
    AllServers
}
