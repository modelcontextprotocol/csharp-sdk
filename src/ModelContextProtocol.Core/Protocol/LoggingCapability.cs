using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the logging capability configuration for a Model Context Protocol server.
/// </summary>
/// <remarks>
/// <para>
/// This capability allows clients to set the logging level and receive log messages from the server.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// <para>
/// This class is intentionally empty as the Model Context Protocol specification does not
/// currently define additional properties for sampling capabilities. Future versions of the
/// specification may extend this capability with additional configuration options.
/// </para>
/// </remarks>
public sealed class LoggingCapability
{
    /// <summary>
    /// Gets or sets the handler for set logging level requests from clients.
    /// </summary>
    [JsonIgnore]
    [Obsolete($"Use {nameof(McpServerHandlers.SetLoggingLevelHandler)} instead.")]
    public McpRequestHandler<SetLevelRequestParams, EmptyResult>? SetLoggingLevelHandler
    {
        get => throw new NotSupportedException($"Use {nameof(McpServerHandlers.SetLoggingLevelHandler)} instead.");
        set => throw new NotSupportedException($"Use {nameof(McpServerHandlers.SetLoggingLevelHandler)} instead.");
    }
}