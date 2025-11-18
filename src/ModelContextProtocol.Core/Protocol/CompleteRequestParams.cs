using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.CompletionComplete"/> request from 
/// a client to ask a server for auto-completion suggestions.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequestMethods.CompletionComplete"/> is used in the Model Context Protocol completion workflow
/// to provide intelligent suggestions for partial inputs related to resources, prompts, or other referenceable entities.
/// The completion mechanism in MCP allows clients to request suggestions based on partial inputs.
/// The server will respond with a <see cref="CompleteResult"/> containing matching values.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class CompleteRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the reference's information.
    /// </summary>
    [JsonPropertyName("ref")]
    public required Reference Ref { get; set; }

    /// <summary>
    /// Gets or sets the argument information for the completion request, specifying what is being completed
    /// and the current partial input.
    /// </summary>
    [JsonPropertyName("argument")]
    public required Argument Argument { get; set; }

    /// <summary>
    /// Gets or sets additional, optional context for completions.
    /// </summary>
    [JsonPropertyName("context")]
    public CompleteContext? Context { get; set; }
}
