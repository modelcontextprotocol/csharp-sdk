using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the server as the payload of "notifications/message" notifications whenever a log message is generated.
/// 
/// If no logging/setLevel request has been sent from the client, the server MAY decide which messages to send automatically.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class LoggingMessageNotificationParams
{
    /// <summary>
    /// The severity of this log message.
    /// </summary>
    [JsonPropertyName("level")]
    public LoggingLevel Level { get; init; }

    /// <summary>
    /// An optional name of the logger issuing this message.
    /// </summary>
    [JsonPropertyName("logger")]
    public string? Logger { get; init; }

    /// <summary>
    /// The data to be logged, such as a string message or an object. Any JSON serializable type is allowed here.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}