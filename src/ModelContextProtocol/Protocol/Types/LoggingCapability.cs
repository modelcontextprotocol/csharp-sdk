using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the logging capability configuration for a Model Context Protocol server.
/// </summary>
/// <remarks>
/// This capability allows clients to set the logging level and receive log messages from the server.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class LoggingCapability
{
    // Currently empty in the spec, but may be extended in the future

    /// <summary>
    /// Gets or sets the handler for set logging level requests from clients.
    /// </summary>
    [JsonIgnore]
    public Func<RequestContext<SetLevelRequestParams>, CancellationToken, Task<EmptyResult>>? SetLoggingLevelHandler { get; set; }
}