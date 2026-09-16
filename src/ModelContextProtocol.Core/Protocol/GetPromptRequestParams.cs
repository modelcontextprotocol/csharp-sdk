using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.PromptsGet"/> request from a client to get a prompt provided by a server.
/// </summary>
/// <remarks>
/// The server will respond with a <see cref="GetPromptResult"/> containing the resulting prompt.
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class GetPromptRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the name of the prompt.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets arguments to use for templating the prompt when retrieving it from the server.
    /// </summary>
    /// <remarks>
    /// Typically, these arguments are used to replace placeholders in prompt templates. The keys in this dictionary
    /// should match the names defined in the prompt's <see cref="Prompt.Arguments"/> list. However, the server can
    /// choose to use these arguments in any way it deems appropriate to generate the prompt.
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IDictionary<string, JsonElement>? Arguments { get; set; }
}
