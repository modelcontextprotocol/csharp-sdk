namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Hints to use for model selection.
/// Keys not declared here are currently left unspecified by the spec and are up
/// to the client to interpret.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <remarks>
/// When multiple hints are specified in <see cref="ModelPreferences.Hints"/>, they are evaluated in order,
/// with the first match taking precedence. Clients should prioritize these hints over numeric priorities.
/// </remarks>
/// <example>
/// <code>
/// // Example of using ModelHint within ModelPreferences
/// var preferences = new ModelPreferences
/// {
///     Hints = new List&lt;ModelHint&gt;
///     {
///         new ModelHint { Name = "claude-3-5-sonnet" }, // Prefer this specific model family
///         new ModelHint { Name = "gpt-4" },             // Fall back to this model family
///     },
///     IntelligencePriority = 0.8f,
///     SpeedPriority = 0.4f
/// };
/// </code>
/// </example>
public class ModelHint
{
    /// <summary>
    /// A hint for a model name.
    /// 
    /// The client SHOULD treat this as a substring of a model name; for example:
    /// - `claude-3-5-sonnet` should match `claude-3-5-sonnet-20241022`
    /// - `sonnet` should match `claude-3-5-sonnet-20241022`, `claude-3-sonnet-20240229`, etc.
    /// - `claude` should match any Claude model
    /// 
    /// The client MAY also map the string to a different provider's model name or a different model family, as long as it fills a similar niche; for example:
    /// - `gemini-1.5-flash` could match `claude-3-haiku-20240307`
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; init; }
}