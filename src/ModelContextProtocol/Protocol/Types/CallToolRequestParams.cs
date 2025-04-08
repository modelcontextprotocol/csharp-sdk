using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Used by the client to invoke a tool provided by the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CallToolRequestParams : RequestParams
{
    /// <summary>
    /// Tool name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Optional arguments to pass to the tool when invoking it on the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This dictionary contains the parameter values to be passed to the tool. Each key-value pair represents 
    /// a parameter name and its corresponding value. The values are stored as <see cref="JsonElement"/> 
    /// to support various parameter types.
    /// </para>
    /// <para>
    /// When a tool is invoked, these arguments will be deserialized into the parameter types
    /// expected by the tool implementation.
    /// </para>
    /// 
    /// <example>
    /// <code>
    /// // Creating a CallToolRequestParams to invoke a weather tool
    /// var requestParams = new CallToolRequestParams
    /// {
    ///     Name = "getWeather",
    ///     Arguments = new Dictionary&lt;string, JsonElement&gt; 
    ///     {
    ///         ["location"] = JsonSerializer.SerializeToElement("New York"),
    ///         ["units"] = JsonSerializer.SerializeToElement("metric")
    ///     }
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
}
