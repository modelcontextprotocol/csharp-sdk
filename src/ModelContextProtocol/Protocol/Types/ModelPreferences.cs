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
    /// How much to prioritize cost when selecting a model. A value of 0 means cost\nis not important, while a value of 1 means cost is the most important
    /// factor.
    /// </summary>
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
    [System.Text.Json.Serialization.JsonPropertyName("speedPriority")]
    public float? SpeedPriority { get; init; }

    /// <summary>
    /// How much to prioritize intelligence and capabilities when selecting a
    /// model. A value of 0 means intelligence is not important, while a value of 1
    /// means intelligence is the most important factor.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("intelligencePriority")]
    public float? IntelligencePriority { get; init; }

    /// <summary>
    /// Validates the model preferences.
    /// </summary>
    /// <param name="errorMessage">Error message if object isn't valid</param>
    /// <returns>True if valid, false if invalid</returns>
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
