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
    /// When set to <c>"complete"</c>, the result is a normal completed response.
    /// When set to <c>"input_required"</c>, the result is an <see cref="InputRequiredResult"/> indicating
    /// that additional input is needed before the request can be completed.
    /// When set to <c>"task"</c>, the result is a <see cref="CreateTaskResult"/> indicating that the server
    /// has created a long-running task in lieu of returning the result directly.
    /// </para>
    /// </remarks>
    /// <value>Defaults to <see langword="null"/>, which is equivalent to <c>"complete"</c>.</value>
    [JsonPropertyName("resultType")]
    public string? ResultType { get; set; }
}
