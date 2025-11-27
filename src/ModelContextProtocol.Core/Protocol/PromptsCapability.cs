using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the server's capability to provide predefined prompt templates that clients can use.
/// </summary>
/// <remarks>
/// <para>
/// The prompts capability allows a server to expose a collection of predefined prompt templates that clients
/// can discover and use. These prompts can be static (defined in the <see cref="McpServerOptions.PromptCollection"/>) or
/// dynamically generated through handlers.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class PromptsCapability
{
    /// <summary>
    /// Gets or sets a value that indicates whether this server supports notifications for changes to the prompt list.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the server will send notifications using
    /// <see cref="NotificationMethods.PromptListChangedNotification"/> when prompts are added,
    /// removed, or modified. Clients can register handlers for these notifications to
    /// refresh their prompt cache. This capability enables clients to stay synchronized with server-side changes
    /// to available prompts.
    /// </remarks>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}
