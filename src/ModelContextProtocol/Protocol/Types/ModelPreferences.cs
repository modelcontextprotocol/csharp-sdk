namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's preferences for model selection, requested of the client during sampling.
/// Because LLMs can vary along multiple dimensions, choosing the \"best\" model is
/// rarely straightforward.  Different models excel in different areas—some are
/// faster but less capable, others are more capable but more expensive, and so
/// on. This interface allows servers to express their priorities across multiple
/// dimensions to help clients make an appropriate selection for their use case.
/// 
/// These preferences are always advisory. The client MAY ignore them. It is also
/// up to the client to decide how to interpret these preferences and how to
/// balance them against other considerations.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class ModelPreferences
{
    /// <summary>
    /// How much to prioritize cost when selecting a model. A value of 0 means cost
    /// is not important, while a value of 1 means cost is the most important factor.
    /// </summary>
    /// <remarks>
    /// Must be a value between 0 and 1. These preferences are advisory and may be ignored by the client.
    /// When balancing multiple priorities, clients may favor less expensive models when this value is high.
    /// </remarks>
    /// <example>
    /// <code>
    /// var preferences = new ModelPreferences
    /// {
    ///     IntelligencePriority = 0.9f, // Strongly prefer intelligence
    ///     CostPriority = 0.3f,         // Lower priority on cost
    ///     SpeedPriority = 0.2f         // Least emphasis on speed
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("costPriority")]
    public float? CostPriority { get; init; }

    /// <summary>
    /// Optional hints to use for model selection.
    /// 
    /// If multiple hints are specified, the client MUST evaluate them in order
    /// (such that the first match is taken).
    /// 
    /// The client SHOULD prioritize these hints over the numeric priorities, but
    /// MAY still use the priorities to select from ambiguous matches.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("hints")]
    public IReadOnlyList<ModelHint>? Hints { get; init; }

    /// <summary>
    /// How much to prioritize sampling speed (latency) when selecting a model. A
    /// value of 0 means speed is not important, while a value of 1 means speed is
    /// the most important factor.
    /// </summary>
    /// <remarks>
    /// Must be a value between 0 and 1. These preferences are advisory and may be ignored by the client.
    /// When balancing multiple priorities, clients may optimize for faster models when this value is high.
    /// </remarks>
    /// <example>
    /// <code>
    /// var preferences = new ModelPreferences
    /// {
    ///     IntelligencePriority = 0.8f, // Higher intelligence
    ///     SpeedPriority = 0.5f         // Moderate emphasis on speed
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("speedPriority")]
    public float? SpeedPriority { get; init; }

    /// <summary>
    /// How much to prioritize intelligence and capabilities when selecting a
    /// model. A value of 0 means intelligence is not important, while a value of 1
    /// means intelligence is the most important factor.
    /// </summary>
    /// <remarks>
    /// Must be a value between 0 and 1. These preferences are advisory and may be ignored by the client.
    /// When balancing multiple priorities (cost, speed, intelligence), clients may weight their selection
    /// based on these relative values.
    /// </remarks>
    /// <example>
    /// <code>
    /// var preferences = new ModelPreferences
    /// {
    ///     IntelligencePriority = 0.8f, // Strongly prefer intelligence/capabilities
    ///     CostPriority = 0.3f,         // Cost is a minor consideration
    ///     SpeedPriority = 0.5f         // Moderate priority for speed
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("intelligencePriority")]
    public float? IntelligencePriority { get; init; }

    /// <summary>
    /// Validates the model preferences to ensure all properties have valid values.
    /// </summary>
    /// <param name="errorMessage">When this method returns false, contains error messages detailing validation failures; otherwise, an empty string.</param>
    /// <returns>True if all preference values are valid; otherwise, false.</returns>
    /// <remarks>
    /// Validation ensures that numeric priority values (CostPriority, SpeedPriority, IntelligencePriority) are between 0 and 1.
    /// </remarks>
    /// <example>
    /// <code>
    /// var preferences = new ModelPreferences
    /// {
    ///     CostPriority = 0.8f,
    ///     SpeedPriority = 0.5f
    /// };
    /// 
    /// if (!preferences.Validate(out string errorMessage))
    /// {
    ///     Console.WriteLine($"Invalid preferences: {errorMessage}");
    /// }
    /// </code>
    /// </example>
    public bool Validate(out string errorMessage)
    {
        bool valid = true;
        List<string> errors = [];
        
        if (CostPriority is < 0 or > 1)
        {
            errors.Add("CostPriority must be between 0 and 1");
            valid = false;
        }
        
        if (SpeedPriority is < 0 or > 1)
        {
            errors.Add("SpeedPriority must be between 0 and 1");
            valid = false;
        }
        
        if (IntelligencePriority is < 0 or > 1)
        {
            errors.Add("IntelligencePriority must be between 0 and 1");
            valid = false;
        }
        
        if (!valid)
        {
            errorMessage = string.Join(", ", errors);
        }
        else
        {
            errorMessage = "";
        }

        return valid;
    }
}
