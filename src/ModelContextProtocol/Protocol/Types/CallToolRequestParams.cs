using ModelContextProtocol.Protocol.Messages;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ToolsCall"/> request from a client to invoke a tool provided by the server.
/// </summary>
/// <remarks>
/// The server will respond with a <see cref="CallToolResponse"/> containing the result of the tool invocation.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class CallToolRequestParams : RequestParams
{
    /// <summary>Gets or sets the name of the tool to invoke.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets optional arguments to pass to the tool when invoking it on the server.
    /// </summary>
    /// <remarks>
    /// This dictionary contains the parameter values to be passed to the tool. Each key-value pair represents 
    /// a parameter name and its corresponding argument value.
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
}
