using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a prompts/list request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ListPromptsResult : PaginatedResult
{
    /// <summary>
    /// A list of prompts or prompt templates that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("prompts")]
    public List<Prompt> Prompts { get; set; } = [];
}