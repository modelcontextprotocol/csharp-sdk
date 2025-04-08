namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a tool call.
/// 
/// Any errors that originate from the tool SHOULD be reported inside the result
/// object, with `isError` set to true, _not_ as an MCP protocol-level error
/// response. Otherwise, the LLM would not be able to see that an error occurred
/// and self-correct.
/// 
/// However, any errors in _finding_ the tool, an error indicating that the
/// server does not support tool calls, or any other exceptional conditions,
/// should be reported as an MCP error response.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CallToolResponse
{
    /// <summary>
    /// The server's response to a tools/call request from the client.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public List<Content> Content { get; set; } = [];

    /// <summary>
    /// Indicates whether the tool call was unsuccessful.
    /// <para>
    /// When set to <c>true</c>, it signifies that the tool execution failed, which can happen due to:
    /// <list type="bullet">
    ///   <item><description>Exceptions thrown during tool execution</description></item>
    ///   <item><description>Missing required parameters</description></item>
    ///   <item><description>Invalid parameter values</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Tool errors are reported with this property set to <c>true</c> and details in the <see cref="Content"/>
    /// property, rather than as protocol-level errors. This allows LLMs to see that an error occurred
    /// and potentially self-correct in subsequent requests.
    /// </para>
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
