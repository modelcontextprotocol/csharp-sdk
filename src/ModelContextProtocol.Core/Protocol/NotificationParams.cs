using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for notification parameters.
/// </summary>
public abstract class NotificationParams
{
    /// <summary>Prevent external derivations.</summary>
    private protected NotificationParams()
    {
    }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Merges additional metadata into the existing Meta object.
    /// </summary>
    /// <param name="additionalMeta">The additional metadata to merge.</param>
    public void MergeMeta(JsonObject additionalMeta)
    {
        if (Meta == null)
        {
            Meta = new JsonObject();
        }

        foreach (var kvp in additionalMeta)
        {
            Meta[kvp.Key] = kvp.Value;
        }
    }
}