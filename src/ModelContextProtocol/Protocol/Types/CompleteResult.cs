using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The server's response to a completion/complete request, containing suggested values for a given argument.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CompleteResult"/> is returned by the server in response to a <c>tools/complete</c> request from the client.
/// It provides suggested completions or valid values for a specific argument in a tool or resource reference.
/// </para>
/// <para>
/// The result contains a <see cref="Completion"/> object with suggested values, pagination information,
/// and the total number of available completions. This is similar to autocompletion functionality in code editors.
/// </para>
/// <para>
/// Clients typically use this to implement auto-suggestion features when users are inputting parameters
/// for tool calls or resource references.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Server-side implementation example
/// .WithCompleteHandler((ctx, ct) =>
/// {
///     var exampleCompletions = new Dictionary&lt;string, IEnumerable&lt;string&gt;&gt;
///     {
///         { "style", ["casual", "formal", "technical", "friendly"] },
///         { "temperature", ["0", "0.5", "0.7", "1.0"] }
///     };
///     
///     if (ctx.Params is not { } @params)
///     {
///         // Return empty results when params are missing
///         return Task.FromResult(new CompleteResult() { Completion = new() { Values = [] } });
///     }
///     
///     var argument = @params.Argument;
///     var argumentName = argument.Name;
///     
///     if (exampleCompletions.TryGetValue(argumentName, out var options))
///     {
///         // Filter values based on what the user has already typed
///         var values = options.Where(v => v.StartsWith(argument.Value)).ToArray();
///         
///         return Task.FromResult(new CompleteResult() 
///         { 
///             Completion = new() 
///             { 
///                 Values = values, 
///                 HasMore = false, 
///                 Total = values.Length 
///             } 
///         });
///     }
///     
///     // No completions found for this argument
///     return Task.FromResult(new CompleteResult());
/// })
/// 
/// // Client-side usage example
/// CompleteResult result = await client.CompleteAsync(
///     new Reference { Type = "ref/tool", Uri = "mcp://tools/weather" },
///     "location",
///     "Seat",  // User typed "Seat"
///     cancellationToken);
///     
/// // Result might contain values like: ["Seattle", "Seatac"]
/// foreach (var suggestion in result.Completion.Values)
/// {
///     Console.WriteLine($"Suggestion: {suggestion}");
/// }
/// </code>
/// </example>
/// <seealso cref="CompleteRequestParams"/>
/// <seealso cref="Completion"/>
/// <seealso cref="CompletionsCapability"/>
/// <seealso href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the MCP schema for details</seealso>
public class CompleteResult
{
    /// <summary>
    /// The completion object containing the suggested values and pagination information.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property contains the suggested completion values for the requested argument.
    /// The <see cref="Completion.Values"/> property provides a list of suggested values that match
    /// the current input, while <see cref="Completion.HasMore"/> and <see cref="Completion.Total"/> 
    /// provide pagination information when there are many suggestions.
    /// </para>
    /// <para>
    /// If no completions are available for the given input, the <see cref="Completion.Values"/> 
    /// collection will be empty.
    /// </para>
    /// </remarks>
    [JsonPropertyName("completion")]
    public Completion Completion { get; set; } = new Completion();
}
