namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request not receiving updated notifications from the server whenever a primitive resource changes.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class UnsubscribeRequestParams : RequestParams
{
    /// <summary>
    /// The URI of the resource to unsubscribe fro. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
