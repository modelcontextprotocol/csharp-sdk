using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a completion/complete request
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class CompleteResult
{
    /// <summary>
    /// The completion object containing the completion values.
    /// </summary>
    [JsonPropertyName("completion")]
    public Completion Completion { get; set; } = new Completion();
}
