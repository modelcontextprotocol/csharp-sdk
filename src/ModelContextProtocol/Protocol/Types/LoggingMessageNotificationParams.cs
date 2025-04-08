using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Sent from the server as the payload of "notifications/message" notifications whenever a log message is generated.
/// 
/// If no logging/setLevel request has been sent from the client, the server MAY decide which messages to send automatically.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// Logging notifications allow servers to communicate diagnostic information to clients with different severity levels.
/// Clients can filter these messages based on the Level and Logger properties.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Server-side code sending a log notification
/// await server.SendNotificationAsync(
///     NotificationMethods.LoggingMessageNotification,
///     new LoggingMessageNotificationParams
///     {
///         Level = LoggingLevel.Info,
///         Logger = "model-server",
///         Data = JsonSerializer.SerializeToElement("Model loaded successfully")
///     });
/// </code>
/// </example>
public class LoggingMessageNotificationParams
{
    /// <summary>
    /// The severity of this log message.
    /// </summary>
    [JsonPropertyName("level")]
    public LoggingLevel Level { get; init; }

    /// <summary>
    /// An optional name of the logger issuing this message. This identifies the source 
    /// component or subsystem that generated the log entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The logger name is useful for filtering and routing log messages in client applications.
    /// It typically represents a category or component in the server's logging system.
    /// </para>
    /// <para>
    /// When implementing custom servers, choose clear, hierarchical logger names to help
    /// clients understand the source of log messages.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example server-side code sending a logging notification
    /// await server.SendNotificationAsync(
    ///     NotificationMethods.LoggingMessageNotification,
    ///     new LoggingMessageNotificationParams 
    ///     {
    ///         Level = LoggingLevel.Info,
    ///         Logger = "model-server",
    ///         Data = JsonSerializer.SerializeToElement("Model loaded successfully")
    ///     });
    /// </code>
    /// </example>
    [JsonPropertyName("logger")]
    public string? Logger { get; init; }

    /// <summary>
    /// The data to be logged, such as a string message or an object.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}