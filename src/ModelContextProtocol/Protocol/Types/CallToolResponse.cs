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
    /// Whether the tool call was unsuccessful. If true, the call was unsuccessful.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
