using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ToolsCall"/> request from a client to invoke a tool provided by the server.
/// </summary>
/// <remarks>
/// The server will respond with a <see cref="CallToolResult"/> containing the result of the tool invocation.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class CallToolRequestParams : RequestParams
{
    /// <summary>Gets or sets the name of the tool to invoke.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets optional arguments to pass to the tool when invoking it on the server.
    /// </summary>
    /// <remarks>
    /// This dictionary contains the parameter values to be passed to the tool. Each key-value pair represents 
    /// a parameter name and its corresponding argument value.
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IDictionary<string, JsonElement>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets optional task metadata to augment this request with task execution.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the requestor wants this operation executed as a task.
    /// The receiver must support task augmentation for this specific request type.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTaskMetadata? Task
    {
        get => TaskCore;
        set => TaskCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("task")]
    internal McpTaskMetadata? TaskCore { get; set; }
}
