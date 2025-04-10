using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the result of a <see cref="RequestMethods.ToolsCall"/> request from a client to invoke a tool provided by the server.
/// </summary>
/// <remarks>
/// <para>
/// Any errors that originate from the tool should be reported inside the result
/// object, with <see cref="IsError"/> set to true, rather than as a <see cref="JsonRpcError"/>.
/// </para>
/// <para>
/// However, any errors in finding the tool, an error indicating that the
/// server does not support tool calls, or any other exceptional conditions,
/// should be reported as an MCP error response.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public class CallToolResponse
{
    /// <summary>
    /// Gets or sets the response content from the tool call.
    /// </summary>
    [JsonPropertyName("content")]
    public List<Content> Content { get; set; } = [];

    /// <summary>
    /// Gets or sets an indication of whether the tool call was unsuccessful.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, it signifies that the tool execution failed.
    /// Tool errors are reported with this property set to <see langword="true"/> and details in the <see cref="Content"/>
    /// property, rather than as protocol-level errors. This allows LLMs to see that an error occurred
    /// and potentially self-correct in subsequent requests.
    /// </remarks>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
