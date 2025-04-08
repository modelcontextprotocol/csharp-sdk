using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Used by the client to get a prompt provided by the server.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
public class GetPromptRequestParams : RequestParams
{
    /// <summary>
    /// he name of the prompt or prompt template.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Arguments to use for templating the prompt when retrieving it from the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These arguments are used to replace placeholders in prompt templates. The keys in this dictionary
    /// should match the names defined in the prompt's <see cref="Prompt.Arguments"/> list.
    /// </para>
    /// <para>
    /// When a prompt is requested, the server will substitute these argument values into
    /// the template placeholders, typically in the format <c>{{argumentName}}</c>.
    /// </para>
    /// 
    /// <example>
    /// <code>
    /// // Requesting a templated prompt
    /// var requestParams = new GetPromptRequestParams
    /// {
    ///     Name = "greeting",
    ///     Arguments = new Dictionary&lt;string, JsonElement&gt;
    ///     {
    ///         ["name"] = JsonSerializer.SerializeToElement("John"),
    ///         ["service"] = JsonSerializer.SerializeToElement("Weather API")
    ///     }
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
}
