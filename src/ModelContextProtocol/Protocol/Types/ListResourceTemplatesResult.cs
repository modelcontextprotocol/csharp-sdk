using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a resources/templates/list request from the client.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ListResourceTemplatesResult : PaginatedResult
{
    /// <summary>
    /// A list of resource templates that the server offers.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("resourceTemplates")]
    public List<ResourceTemplate> ResourceTemplates { get; set; } = [];
}