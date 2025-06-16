using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a message issued to or received from an LLM API within the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SamplingMessage"/> encapsulates content sent to or received from AI models in the Model Context Protocol.
/// Each message has a specific role (<see cref="Role.User"/> or <see cref="Role.Assistant"/>) and contains content which can be text or images.
/// </para>
/// <para>
/// <see cref="SamplingMessage"/> objects are typically used in collections within <see cref="CreateMessageRequestParams"/>
/// to represent prompts or queries for LLM sampling. They form the core data structure for text generation requests
/// within the Model Context Protocol.
/// </para>
/// <para>
/// While similar to <see cref="PromptMessage"/>, the <see cref="SamplingMessage"/> is focused on direct LLM sampling
/// operations rather than the enhanced resource embedding capabilities provided by <see cref="PromptMessage"/>.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class SamplingMessage
{
    /// <summary>
    /// Gets or sets the content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public required ContentBlock Content { get; init; }

    /// <summary>
    /// Gets or sets the role of the message sender, indicating whether it's from a "user" or an "assistant".
    /// </summary>
    [JsonPropertyName("role")]
    public required Role Role { get; init; }
}
