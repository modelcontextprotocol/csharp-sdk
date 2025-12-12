using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a server's preferences for model selection, requested of the client during sampling.
/// </summary>
/// <remarks>
/// <para>
/// Because LLMs can vary along multiple dimensions, choosing the "best" model is
/// rarely straightforward.  Different models excel in different areas—some are
/// faster but less capable, others are more capable but more expensive, and so
/// on. This class allows servers to express their priorities across multiple
/// dimensions to help clients make an appropriate selection for their use case.
/// </para>
/// <para>
/// These preferences are always advisory. The client may ignore them. It is also
/// up to the client to decide how to interpret these preferences and how to
/// balance them against other considerations.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class ModelPreferences
{
    /// <summary>
    /// Gets or sets a value that indicates how much to prioritize cost when selecting a model.
    /// </summary>
    /// <remarks>
    /// A value of 0 means cost is not important, while a value of 1 means cost is the most important factor.
    /// </remarks>
    [JsonPropertyName("costPriority")]
    public float? CostPriority { get; set; }

    /// <summary>
    /// Gets or sets optional hints to use for model selection.
    /// </summary>
    [JsonPropertyName("hints")]
    public IList<ModelHint>? Hints { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates how much to prioritize sampling speed (latency) when selecting a model.
    /// </summary>
    /// <remarks>
    /// A value of 0 means speed is not important, while a value of 1 means speed is the most important factor.
    /// </remarks>
    [JsonPropertyName("speedPriority")]
    public float? SpeedPriority { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates how much to prioritize intelligence and capabilities when selecting a model.
    /// </summary>
    /// <remarks>
    /// A value of 0 means intelligence is not important, while a value of 1 means intelligence is the most important factor.
    /// </remarks>
    [JsonPropertyName("intelligencePriority")]
    public float? IntelligencePriority { get; set; }
}
