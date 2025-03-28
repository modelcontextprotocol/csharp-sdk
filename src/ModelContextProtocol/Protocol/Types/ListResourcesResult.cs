using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/list request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ListResourcesResult : PaginatedResult
{
    /// <summary>
    /// A list of resources that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("resources")]
    public List<Resource> Resources { get; set; } = [];
}
