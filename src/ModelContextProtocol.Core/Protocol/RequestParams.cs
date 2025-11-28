using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for all request parameters.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public abstract class RequestParams
{
    /// <summary>Prevent external derivations.</summary>
    private protected RequestParams()
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
    /// Gets the opaque token that will be attached to any subsequent progress notifications.
    /// </summary>
    [JsonIgnore]
    public ProgressToken? ProgressToken
    {
        get
        {
            if (Meta?["progressToken"] is JsonValue progressToken)
            {
                if (progressToken.TryGetValue(out string? stringValue))
                {
                    return new ProgressToken(stringValue);
                }

                if (progressToken.TryGetValue(out long longValue))
                {
                    return new ProgressToken(longValue);
                }
            }

            return null;
        }
    }
}
