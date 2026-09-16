using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a message sent from the client to cancel resource update notifications from the server for a specific resource.
/// </summary>
/// <remarks>
/// <para>
/// After a client has subscribed to resource updates using <see cref="SubscribeRequestParams"/>,
/// this message can be sent to stop receiving notifications for a specific resource.
/// This action is useful for conserving resources and network bandwidth when
/// the client no longer needs to track changes to a particular resource.
/// </para>
/// <para>
/// The unsubscribe operation is idempotent, meaning it can be called multiple times
/// for the same resource without causing errors, even if there is no active subscription.
/// </para>
/// </remarks>
public sealed class UnsubscribeRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the URI of the resource to unsubscribe from. The URI can use any protocol; it is up to the server how to interpret it.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }
}
