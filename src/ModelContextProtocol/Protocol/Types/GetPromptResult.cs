using ModelContextProtocol.Protocol.Messages;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents a server's response to a <see cref="RequestMethods.PromptsGet"/> request from the client.
/// </summary>
/// <remarks>
/// <para>
/// For integration with AI client libraries, <see cref="GetPromptResult"/> can be converted to
/// a collection of <see cref="ChatMessage"/> objects using the <see cref="AIContentExtensions.ToChatMessages"/> extension method.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public class GetPromptResult
{
    /// <summary>
    /// Gets or sets an optional description for the prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description provides contextual information about the prompt's purpose and use cases.
    /// It helps developers understand what the prompt is designed for and how it should be used.
    /// </para>
    /// <para>
    /// When returned from a server in response to a <see cref="RequestMethods.PromptsGet"/> request,
    /// this description can be used by client applications to provide context about the prompt or to
    /// display in user interfaces.
    /// </para>
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the prompt that the server offers.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<PromptMessage> Messages { get; set; } = [];
}
