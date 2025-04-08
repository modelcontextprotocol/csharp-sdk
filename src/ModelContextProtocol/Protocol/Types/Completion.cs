using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a completion object in the server's response
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class Completion
{
    /// <summary>
    /// An array of completion values (autosuggestions) for the requested input. Must not exceed 100 items.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This collection contains the actual text strings to be presented to users as completion suggestions.
    /// For example, when a user is typing a parameter value, these strings represent the valid options
    /// or matches based on what they've typed so far.
    /// </para>
    /// <para>
    /// The array will be empty if no suggestions are available for the current input.
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Server-side example (providing completions):</para>
    /// <code>
    /// // Filtering values based on user input
    /// var values = options.Where(v => v.StartsWith(argument.Value)).ToArray();
    /// 
    /// return Task.FromResult(new CompleteResult() { 
    ///     Completion = new Completion { 
    ///         Values = [..values], 
    ///         HasMore = false, 
    ///         Total = values.Count() 
    ///     }
    /// });
    /// </code>
    /// 
    /// <para>Client-side example (using completions):</para>
    /// <code>
    /// // Process suggestions from the completion result
    /// foreach (var suggestion in result.Completion.Values)
    /// {
    ///     Console.WriteLine($"Suggestion: {suggestion}");
    /// }
    /// </code>
    /// </example>
    [JsonPropertyName("values")]
    public string[] Values { get; set; } = [];

    /// <summary>
    /// The total number of completion options available. This can exceed the number of values actually sent in the response.
    /// </summary>
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    /// <summary>
    /// Indicates whether there are additional completion options beyond those provided in the current response, even if the exact total is unknown.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool? HasMore { get; set; }
}
