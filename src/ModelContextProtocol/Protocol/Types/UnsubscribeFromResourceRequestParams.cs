namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the client to request cancellation of resources/updated notifications from the server. This should follow a previous resources/subscribe request.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class UnsubscribeFromResourceRequestParams
{
    /// <summary>
    /// The URI of the resource to unsubscribe from.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("uri")]
    public string? Uri { get; init; }
}
