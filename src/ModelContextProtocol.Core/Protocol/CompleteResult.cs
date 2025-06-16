using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the server's response to a <see cref="RequestMethods.CompletionComplete"/> request, 
/// containing suggested values for a given argument.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompleteResult"/> is returned by the server in response to a <see cref="RequestMethods.CompletionComplete"/> 
/// request from the client. It provides suggested completions or valid values for a specific argument in a tool or resource reference.
/// </para>
/// <para>
/// The result contains a <see cref="Completion"/> object with suggested values, pagination information,
/// and the total number of available completions. This is similar to auto-completion functionality in code editors.
/// </para>
/// <para>
/// Clients typically use this to implement auto-suggestion features when users are inputting parameters
/// for tool calls or resource references.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class CompleteResult : Result
{
    /// <summary>
    /// Gets or sets the completion object containing the suggested values and pagination information.
    /// </summary>
    /// <remarks>
    /// If no completions are available for the given input, the <see cref="Completion.Values"/> 
    /// collection will be empty.
    /// </remarks>
    [JsonPropertyName("completion")]
    public Completion Completion { get; set; } = new Completion();
}
