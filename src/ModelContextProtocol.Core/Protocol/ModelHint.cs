using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides hints to use for model selection.
/// </summary>
/// <remarks>
/// <para>
/// When multiple hints are specified in <see cref="ModelPreferences.Hints"/>, they are evaluated in order,
/// with the first match taking precedence. Clients should prioritize these hints over numeric priorities.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
// Sampling support type: only used inside ModelPreferences to hint a model for sampling (createMessage)
// requests, so it is deprecated together with sampling per SEP-2577.
[Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
public sealed class ModelHint
{
    /// <summary>
    /// Gets or sets a hint for a model name.
    /// </summary>
    /// <remarks>
    /// The specified string can be a partial or full model name. Clients can also
    /// map hints to equivalent models from different providers. Clients make the final model
    /// selection based on these preferences and their available models.
    /// </remarks>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
