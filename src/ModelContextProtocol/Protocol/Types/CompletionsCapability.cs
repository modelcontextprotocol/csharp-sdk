using ModelContextProtocol.Server;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the completions capability configuration for providing auto-completion suggestions
/// for prompt arguments and resource references.
/// </summary>
/// <remarks>
/// <para>
/// When enabled, this capability allows a Model Context Protocol server to provide 
/// auto-completion suggestions to clients while they're typing prompt arguments or 
/// resource references. This capability is advertised to clients during the initialize handshake.
/// </para>
/// <para>
/// The primary function of this capability is to improve the user experience by offering
/// contextual suggestions for argument values or resource identifiers based on partial input.
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Configure completions capability with a handler
/// var completionsCapability = new CompletionsCapability
/// {
///     CompleteHandler = (request, cancellationToken) =>
///     {
///         Dictionary&lt;string, string[]&gt; exampleCompletions = new()
///         {
///             {"style", ["casual", "formal", "technical"]},
///             {"temperature", ["0", "0.7", "1.0"]},
///         };
///         
///         if (request.Params?.Ref?.Type == "ref/prompt")
///         {
///             // Handle completion for prompt arguments
///             if (!exampleCompletions.TryGetValue(request.Params.Argument.Name, out var completions))
///                 return Task.FromResult(new CompleteResult() { Completion = new() { Values = [] } });
///             
///             var values = completions.Where(value => value.StartsWith(request.Params.Argument.Value)).ToArray();
///             return Task.FromResult(new CompleteResult() 
///             { 
///                 Completion = new() 
///                 { 
///                     Values = values, 
///                     HasMore = false, 
///                     Total = values.Length 
///                 } 
///             });
///         }
///         
///         return Task.FromResult(new CompleteResult());
///     }
/// };
/// </code>
/// </example>
public class CompletionsCapability
{
    // Currently empty in the spec, but may be extended in the future.

    /// <summary>
    /// Gets or sets the handler for completion requests. This handler provides auto-completion suggestions
    /// for prompt arguments or resource references in the Model Context Protocol.
    /// </summary>
    /// <remarks>
    /// The handler receives a reference type (e.g., "ref/prompt" or "ref/resource") and the current argument value,
    /// and should return appropriate completion suggestions.
    /// </remarks>
    [JsonIgnore]
    public Func<RequestContext<CompleteRequestParams>, CancellationToken, Task<CompleteResult>>? CompleteHandler { get; set; }
}