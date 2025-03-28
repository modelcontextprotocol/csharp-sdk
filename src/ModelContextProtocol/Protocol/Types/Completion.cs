using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a completion object in the server's response
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Completion
{
    /// <summary>
    /// An array of completion values. Must not exceed 100 items.
    /// </summary>
    [JsonPropertyName("values")]
    public string[] Values { get; set; } = [];

    /// <summary>
    /// The total number of completion options available. This can exceed the number of values actually sent in the response.
    /// </summary>
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    /// <summary>
    /// Indicates whether there are additional completion options beyond those provided in the current response, even if the exact total is unknown.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool? HasMore { get; set; }
}
