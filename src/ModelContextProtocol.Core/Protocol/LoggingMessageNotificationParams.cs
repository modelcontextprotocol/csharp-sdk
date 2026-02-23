using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.LoggingMessageNotification"/>
/// notification sent whenever a log message is generated.
/// </summary>
/// <remarks>
/// <para>
/// Logging notifications allow servers to communicate diagnostic information to clients with varying severity levels.
/// Clients can filter these messages based on the <see cref="Level"/> and <see cref="Logger"/> properties.
/// </para>
/// <para>
/// If no <see cref="RequestMethods.LoggingSetLevel"/> request has been sent from the client, the server can decide which
/// messages to send automatically.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class LoggingMessageNotificationParams : NotificationParams
{
    /// <summary>
    /// Gets or sets the severity of this log message.
    /// </summary>
    [JsonPropertyName("level")]
    public required LoggingLevel Level { get; set; }

    /// <summary>
    /// Gets or sets an optional name of the logger issuing this message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Logger"/> typically represents a category or component in the server's logging system.
    /// The logger name is useful for filtering and routing log messages in client applications.
    /// </para>
    /// <para>
    /// When implementing custom servers, choose clear, hierarchical logger names to help
    /// clients understand the source of log messages.
    /// </para>
    /// </remarks>
    [JsonPropertyName("logger")]
    public string? Logger { get; set; }

    /// <summary>
    /// Gets or sets the data to be logged, such as a string message or an object.
    /// Any JSON serializable type is allowed here.
    /// </summary>
    [JsonPropertyName("data")]
    public required JsonElement Data { get; set; }
}
