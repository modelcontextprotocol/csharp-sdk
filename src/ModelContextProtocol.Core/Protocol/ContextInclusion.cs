using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Specifies the context inclusion options for a request in the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// <para>
/// <see cref="ContextInclusion"/>, and in particular <see cref="ThisServer"/> and <see cref="AllServers"/>, are deprecated.
/// Servers should only use these values if the client declares <see cref="ClientCapabilities.Sampling"/> with
/// <see cref="SamplingCapability.Context"/> set. These values might be removed in future spec releases.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ContextInclusion>))]
public enum ContextInclusion
{
    /// <summary>
    /// No context should be included.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// Context from the server that sent the request should be included.
    /// </summary>
    /// <remarks>
    /// This value is soft-deprecated. Servers should only use this value if the client
    /// declares ClientCapabilities.Sampling.Context.
    /// </remarks>
    [JsonStringEnumMemberName("thisServer")]
    ThisServer,

    /// <summary>
    /// Context from all servers that the client is connected to should be included.
    /// </summary>
    /// <remarks>
    /// This value is soft-deprecated. Servers should only use this value if the client
    /// declares ClientCapabilities.Sampling.Context.
    /// </remarks>
    [JsonStringEnumMemberName("allServers")]
    AllServers
}
