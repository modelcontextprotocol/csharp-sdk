using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request from the client to the server, to enable or adjust logging.
/// Used to control the verbosity level of logs sent from server to client.
/// </summary>
/// <remarks>
/// <para>
/// This request allows clients to configure the level of logging information they want to receive from the server.
/// The server will send notifications for log events at the specified level and all higher (more severe) levels.
/// </para>
/// <para>
/// Log levels follow standard severity ordering: Off > Fatal > Error > Warning > Information > Debug > Trace,
/// where higher levels are more restrictive (fewer messages) and lower levels are more verbose (more messages).
/// </para>
/// <para>
/// When a client sets the log level, the server should adjust its notification behavior
/// for the duration of the connection to respect this setting.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Turn on debug-level logging
/// await client.SetLoggingLevelAsync(LoggingLevel.Debug);
///
/// // Reduce verbosity to only see warnings and above
/// await client.SetLoggingLevelAsync(LoggingLevel.Warning);
/// 
/// // Turn off logging completely
/// await client.SetLoggingLevelAsync(LoggingLevel.Off);
/// </code>
/// </example>
/// <seealso cref="LoggingLevel"/>
/// <seealso cref="LoggingCapability"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class SetLevelRequestParams : RequestParams
{
    /// <summary>
    /// The level of logging that the client wants to receive from the server. 
    /// The server should send all logs at this level and higher (i.e., more severe) to the client as notifications/message.
    /// </summary>
    [JsonPropertyName("level")]
    public required LoggingLevel Level { get; init; }
}
