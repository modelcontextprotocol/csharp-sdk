using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents an argument used in completion requests to provide context for auto-completion functionality.
/// </summary>
/// <remarks>
/// This class is used when requesting completion suggestions for a particular field or parameter.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class Argument
{
    /// <summary>
    /// Gets or sets the name of the argument being completed.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the current partial text value for which completion suggestions are requested.
    /// </summary>
    /// <remarks>
    /// This property represents the text that has been entered so far and for which completion
    /// options should be generated.
    /// </remarks>
    [JsonPropertyName("value")]
    public required string Value { get; set; }
}
