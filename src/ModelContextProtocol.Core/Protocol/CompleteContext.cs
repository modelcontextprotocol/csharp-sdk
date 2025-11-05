using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents additional context information for completion requests.
/// </summary>
/// <remarks>
/// This context provides information that helps the server generate more relevant 
/// completion suggestions, such as previously resolved variables in a template.
/// </remarks>
public sealed class CompleteContext
{
    /// <summary>
    /// Gets or sets previously-resolved variables in a URI template or prompt.
    /// </summary>
    [JsonPropertyName("arguments")]
    public IDictionary<string, string>? Arguments { get; set; }
}
