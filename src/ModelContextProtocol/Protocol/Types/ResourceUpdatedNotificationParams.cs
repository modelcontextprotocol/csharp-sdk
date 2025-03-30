namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the server as the payload of "notifications/resources/updated" notifications whenever a subscribed resource changes.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ResourceUpdatedNotificationParams
{
    /// <summary>
    /// The URI of the resource to subscribe to. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
