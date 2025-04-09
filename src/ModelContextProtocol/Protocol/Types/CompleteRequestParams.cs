namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request from the client to the server to ask for auto-completion suggestions.
/// Used in the Model Context Protocol completion workflow to provide intelligent suggestions
/// for partial inputs related to resources, prompts, or other referenceable entities.
/// </summary>
/// <remarks>
/// <para>
/// The completion mechanism in MCP allows clients to request suggestions based on partial inputs.
/// This is commonly used for:
/// </para>
/// <list type="bullet">
///   <item>Resource ID completion</item>
///   <item>Prompt argument value completion</item>
///   <item>Other parameter value autocompletion</item>
/// </list>
/// <para>
/// The server will respond with a <see cref="CompleteResult"/> containing matching values.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Request completion for a resource reference
/// var completionRequest = new CompleteRequestParams
/// {
///     Ref = new Reference 
///     { 
///         Type = "ref/resource",
///         Uri = "test://resource/" 
///     },
///     Argument = new Argument
///     {
///         Name = "id",
///         Value = "2"  // Partial value for which to get completions
///     }
/// };
/// 
/// // The server would respond with values like ["2", "20", "21", ...] that match the partial "2" input
/// </code>
/// </example>
/// <seealso cref="CompleteResult"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</seealso>
public class CompleteRequestParams : RequestParams
{
    /// <summary>
    /// The reference's information
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("ref")]
    public required Reference Ref { get; init; }

    /// <summary>
    /// The argument information for the completion request, specifying what is being completed
    /// and the current partial input.
    /// </summary>
    /// <remarks>
    /// This field contains the name of the parameter being completed (e.g., "modelName")
    /// and the current partial value entered by the user (e.g., "gpt-") for which
    /// completion suggestions are requested.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("argument")]
    public required Argument Argument { get; init; }    
}
