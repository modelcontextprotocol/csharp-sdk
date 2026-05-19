using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for result payloads.
/// </summary>
public abstract class Result
{
    /// <summary>Prevent external derivations.</summary>
    private protected Result()
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
    /// When set to <c>"incomplete"</c>, the result is an <see cref="IncompleteResult"/> indicating
    /// that additional input is needed before the request can be completed.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <see langword="null"/>, which is equivalent to <c>"complete"</c>.</value>
    [JsonPropertyName("result_type")]
    public string? ResultType { get; set; }
}
