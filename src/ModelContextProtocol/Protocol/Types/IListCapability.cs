using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the tools capability configuration.
/// </summary>
/// <typeparam name="TPrimitive">The type of the primitive.</typeparam>
internal interface IListCapability<TPrimitive>
    where TPrimitive : IMcpServerPrimitive
{
    /// <summary>
    /// Gets or sets whether this server supports notifications for changes to the tool list.
    /// </summary>
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
    
    /// <summary>
    /// Gets or sets the handler for list tools requests.
    /// </summary>
    [JsonIgnore]
    public McpServerPrimitiveCollection<TPrimitive>? Collection { get; set; }
}
