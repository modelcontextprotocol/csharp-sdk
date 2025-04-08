using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents an argument used in completion requests to provide context for auto-completion functionality.
/// This class is used when requesting completion suggestions for a particular field or parameter.
/// </summary>
/// <remarks>
/// The <see cref="Argument"/> class consists of a name-value pair where:
/// <list type="bullet">
///   <item><description>The <see cref="Name"/> identifies the parameter or field being completed</description></item>
///   <item><description>The <see cref="Value"/> contains the current partial text that should be matched against possible completions</description></item>
/// </list>
/// <para>
/// Example usage:
/// <code>
/// // Request completions for a parameter named "modelName" with current input "gpt-"
/// var completionRequest = new CompleteRequestParams
/// {
///     Ref = reference,
///     Argument = new Argument { Name = "modelName", Value = "gpt-" }
/// };
/// </code>
/// </para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </remarks>
public class Argument
{
    /// <summary>
    /// The name of the argument being completed.
    /// </summary>
    /// <remarks>
    /// This identifies which parameter, field, or option is currently being completed.
    /// For example, when completing a model name, this might be "modelName".
    /// </remarks>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The current partial text value for which completion suggestions are requested.
    /// </summary>
    /// <remarks>
    /// This represents the text that has been entered so far and for which completion
    /// options should be generated. For example, if a user has typed "gpt-" and wants
    /// autocomplete suggestions, this value would be "gpt-".
    /// </remarks>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}