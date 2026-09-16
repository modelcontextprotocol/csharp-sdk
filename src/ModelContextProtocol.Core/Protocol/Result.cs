using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for result payloads.
/// </summary>
public abstract class Result
{
    /// <summary>Initializes the base result type.</summary>
    protected Result()
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
    /// Gets or sets the type of the result, which allows the client to determine how to parse the result object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When absent or set to <c>"complete"</c>, the result is a normal completed response.
    /// Other values discriminate alternate result subtypes so callers can choose the appropriate
    /// concrete payload to deserialize.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <see langword="null"/>, which is equivalent to <c>"complete"</c>.</value>
    [JsonPropertyName("resultType")]
    public string? ResultType { get; set; }
}
