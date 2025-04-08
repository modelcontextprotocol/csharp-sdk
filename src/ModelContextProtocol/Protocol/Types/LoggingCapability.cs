using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the logging capability configuration for a ModelContextProtocol server.
/// This capability allows clients to set the logging level and receive log messages from the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// <para>
/// When a server supports the logging capability, clients can:
/// - Set a minimum logging level with the <c>logging/setLevel</c> request
/// - Receive log messages via <c>notifications/message</c> notifications
/// </para>
/// <para>
/// The server will only send log messages that are at or above the specified level.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Server-side configuration:
/// var builder = new McpServerBuilder();
/// 
/// // Configure logging capability
/// builder.WithSetLoggingLevelHandler(async (ctx, ct) =>
/// {
///     _minimumLoggingLevel = ctx.Params.Level;
///     await ctx.Server.SendNotificationAsync("notifications/message", new LoggingMessageNotificationParams
///     {
///         Level = LoggingLevel.Debug,
///         Data = JsonSerializer.SerializeToElement($"Logging level set to {_minimumLoggingLevel}")
///     });
///     return new EmptyResult();
/// });
/// </code>
/// </example>
public class LoggingCapability
{
    // Currently empty in the spec, but may be extended in the future


    /// <summary>
    /// Gets or sets the handler for set logging level requests from clients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler is invoked when a client sends a <c>logging/setLevel</c> request. The handler
    /// should update the server's minimum logging level threshold according to the requested level.
    /// </para>
    /// <para>
    /// After changing the level, the server should begin sending log messages of the specified level
    /// (and higher) to the client via <c>notifications/message</c> notifications.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Simple implementation example:
    /// LoggingLevel _minimumLoggingLevel = LoggingLevel.Warning;
    /// 
    /// loggingCapability.SetLoggingLevelHandler = async (ctx, ct) => 
    /// {
    ///     // Update the internal logging level threshold
    ///     _minimumLoggingLevel = ctx.Params.Level;
    ///     
    ///     // Optionally confirm the change with a notification
    ///     await ctx.Server.SendNotificationAsync(
    ///         NotificationMethods.LoggingMessageNotification,
    ///         new LoggingMessageNotificationParams
    ///         {
    ///             Level = LoggingLevel.Info,
    ///             Logger = "server",
    ///             Data = JsonSerializer.SerializeToElement($"Logging level set to {_minimumLoggingLevel}")
    ///         });
    ///     
    ///     return new EmptyResult();
    /// };
    /// </code>
    /// </example>
    [JsonIgnore]
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>>? SetLoggingLevelHandler { get; set; }
}