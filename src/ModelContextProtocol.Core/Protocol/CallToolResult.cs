using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <see cref="RequestMethods.ToolsCall"/> request from a client to invoke a tool provided by the server.
/// </summary>
/// <remarks>
/// <para>
/// Tool execution errors (including input validation errors, API failures, and business logic errors)
/// should be reported inside the result object with <see cref="IsError"/> set to <see langword="true"/>,
/// rather than as a <see cref="JsonRpcError"/>. This allows language models to see error details
/// and potentially self-correct in subsequent requests.
/// </para>
/// <para>
/// To return a validation or business-logic error from a tool method, either throw an <see cref="McpException"/>
/// (whose <see cref="Exception.Message"/> will be included in the error result), or declare the tool's return type
/// as <see cref="CallToolResult"/> so it can be returned directly with <see cref="IsError"/> set to <see langword="true"/>
/// and details in <see cref="Content"/>. Using <see cref="CallToolResult"/> as the return type gives the tool full control
/// over both success and error responses.
/// </para>
/// <para>
/// Protocol-level errors (such as unknown tool names, malformed requests that fail schema validation,
/// or server errors) should be reported as MCP protocol error responses using <see cref="McpErrorCode"/>.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class CallToolResult : Result
{
    /// <summary>
    /// Gets or sets the response content from the tool call.
    /// </summary>
    [JsonPropertyName("content")]
    public IList<ContentBlock> Content { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional JSON object representing the structured result of the tool call.
    /// </summary>
    [JsonPropertyName("structuredContent")]
    public JsonElement? StructuredContent { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool call was unsuccessful.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to signify that the tool execution failed; <see langword="false"/> if it was successful.
    /// </value>
    /// <remarks>
    /// <para>
    /// Tool execution errors (including input validation errors, API failures, and business logic errors)
    /// are reported with this property set to <see langword="true"/> and details in the <see cref="Content"/>
    /// property, rather than as protocol-level errors.
    /// </para>
    /// <para>
    /// This design allows language models to receive detailed error feedback and potentially self-correct
    /// in subsequent requests. For example, if a date parameter is in the wrong format or out of range,
    /// the error message in <see cref="Content"/> can explain the issue, enabling the model to retry
    /// with corrected parameters.
    /// </para>
    /// </remarks>
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }

    /// <summary>
    /// Gets or sets the task data for the newly created task.
    /// </summary>
    /// <remarks>
    /// This property is populated only for task-augmented tool calls. When present, the other properties
    /// (<see cref="Content"/>, <see cref="StructuredContent"/>, <see cref="IsError"/>) may not be populated.
    /// The actual tool result can be retrieved later via <c>tasks/result</c>.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTask? Task
    {
        get => TaskCore;
        set => TaskCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("task")]
    internal McpTask? TaskCore { get; set; }
}
