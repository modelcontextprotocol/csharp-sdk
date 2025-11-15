using System.Text.Json.Nodes;
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
    public JsonNode? StructuredContent { get; set; }

    /// <summary>
    /// Gets or sets an indication of whether the tool call was unsuccessful.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set to <see langword="true"/>, it signifies that the tool execution failed.
    /// Tool execution errors (including input validation errors, API failures, and business logic errors)
    /// are reported with this property set to <see langword="true"/> and details in the <see cref="Content"/>
    /// property, rather than as protocol-level errors.
    /// </para>
    /// <para>
    /// This allows language models to receive detailed error feedback and potentially self-correct
    /// in subsequent requests. For example, if a date parameter is in the wrong format or out of range,
    /// the error message in <see cref="Content"/> can explain the issue, enabling the model to retry
    /// with corrected parameters.
    /// </para>
    /// </remarks>
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }
}
