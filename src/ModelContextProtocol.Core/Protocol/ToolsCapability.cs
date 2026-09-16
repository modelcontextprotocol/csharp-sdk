using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the tools capability configuration.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </summary>
public sealed class ToolsCapability
{
    /// <summary>
    /// Gets or sets a value that indicates whether this server supports notifications for changes to the tool list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the server sends notifications using
    /// <see cref="NotificationMethods.ToolListChangedNotification"/> when tools are added,
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their tool cache. This capability enables clients to stay synchronized with server-side
    /// changes to available tools.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}
