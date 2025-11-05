using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.ResourceUpdatedNotification"/>
/// notification sent whenever a subscribed resource changes.
/// </summary>
/// <remarks>
/// <para>
/// When a client subscribes to resource updates using <see cref="SubscribeRequestParams"/>, the server will
/// send notifications with this payload whenever the subscribed resource is modified. These notifications
/// allow clients to maintain synchronized state without needing to poll the server for changes.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ResourceUpdatedNotificationParams : NotificationParams
{
    /// <summary>
    /// Gets or sets the URI of the resource that was updated.
    /// </summary>
    /// <remarks>
    /// The URI can use any protocol; it is up to the server how to interpret it.
    /// </remarks>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }
}
