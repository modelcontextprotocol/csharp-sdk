using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.ResourcesSubscribe"/> request from a client
/// to request real-time notifications from the server whenever a particular resource changes.
/// </summary>
/// <remarks>
/// <para>
/// The subscription mechanism allows clients to be notified about changes to specific resources
/// identified by their URI. When a subscribed resource changes, the server sends a notification
/// to the client with the updated resource information.
/// </para>
/// <para>
/// Subscriptions remain active until explicitly canceled using <see cref="UnsubscribeRequestParams"/>
/// or until the connection is terminated.
/// </para>
/// <para>
/// The server might refuse or limit subscriptions based on its capabilities or resource constraints.
/// </para>
/// </remarks>
public sealed class SubscribeRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the URI of the resource to subscribe to.
    /// </summary>
    /// <remarks>
    /// The URI can use any protocol; it is up to the server how to interpret it.
    /// </remarks>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }
}
