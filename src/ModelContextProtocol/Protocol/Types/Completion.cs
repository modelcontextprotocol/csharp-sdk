using ModelContextProtocol.Protocol.Messages;

using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a completion object in the server's response to a <see cref="RequestMethods.CompletionComplete"/> request.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class Completion
{
    /// <summary>
    /// Gets or sets an array of completion values (auto-suggestions) for the requested input.
    /// </summary>
    /// <remarks>
    /// This collection contains the actual text strings to be presented to users as completion suggestions.
    /// The array will be empty if no suggestions are available for the current input.
    /// Per the specification, this should not exceed 100 items.
    /// </remarks>
    [JsonPropertyName("values")]
    public string[] Values { get; set; } = [];

    /// <summary>
    /// Gets or sets the total number of completion options available.
    /// </summary>
    /// <remarks>
    /// This can exceed the number of values actually sent in the response.
    /// </remarks>
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    /// <summary>
    /// Gets or sets an indicator as to whether there are additional completion options beyond 
    /// those provided in the current response, even if the exact total is unknown.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool? HasMore { get; set; }
}
