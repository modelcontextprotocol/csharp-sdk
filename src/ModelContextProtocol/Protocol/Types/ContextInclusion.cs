using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request to include context from one or more MCP servers (including the caller), to be attached to the prompt.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContextInclusion>))]
public enum ContextInclusion
{
    /// <summary>
    /// No context should be included.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// Include context from the server that sent the request.
    /// </summary>
    [JsonStringEnumMemberName("thisServer")]
    ThisServer,

    /// <summary>
    /// Include context from all servers that the client is connected to.
    /// </summary>
    [JsonStringEnumMemberName("allServers")]
    AllServers
}
