using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Describes a message issued to or received from an LLM API within the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SamplingMessage"/> encapsulates content sent to or received from AI models in the Model Context Protocol.
/// Each message has a specific role (user or assistant) and contains content which can be text or images.
/// </para>
/// 
/// <para>
/// <see cref="SamplingMessage"/> objects are typically used in collections within <see cref="CreateMessageRequestParams"/>
/// to represent prompts or queries for LLM sampling. They form the core data structure for text generation requests
/// within the Model Context Protocol.
/// </para>
/// 
/// <para>
/// While similar to <see cref="PromptMessage"/>, the <see cref="SamplingMessage"/> is focused on direct LLM sampling
/// operations rather than the enhanced resource embedding capabilities provided by <see cref="PromptMessage"/>.
/// </para>
/// 
/// <para>
/// For more details on the schema and protocol specifications, see:
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">Model Context Protocol Schema</see>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create a user message for LLM sampling
/// var userMessage = new SamplingMessage
/// {
///     Role = Role.User,
///     Content = new Content
///     {
///         Type = "text",
///         Text = "Explain quantum computing in simple terms"
///     }
/// };
/// 
/// // Use in CreateMessageRequestParams for sampling
/// var requestParams = new CreateMessageRequestParams
/// {
///     Messages = [userMessage],
///     SystemPrompt = "You are a helpful assistant.",
///     MaxTokens = 100,
///     Temperature = 0.7f
/// };
/// </code>
/// </example>
public class SamplingMessage
{
    /// <summary>
    /// Gets or sets the content of the message, which can be text or image data.
    /// </summary>
    /// <remarks>
    /// The <see cref="Content"/> object encapsulates the message payload and defines its format
    /// through the <see cref="Content.Type"/> property. Commonly used types include "text" for
    /// textual content and image formats for visual content.
    /// </remarks>
    [JsonPropertyName("content")]
    public required Content Content { get; init; }

    /// <summary>
    /// Gets or sets the role of the message sender, indicating whether it's from a "user" or an "assistant".
    /// </summary>
    /// <remarks>
    /// The role property establishes the message's context within a conversation flow:
    /// <list type="bullet">
    /// <item><see cref="Role.User"/> indicates a message from the user/client</item>
    /// <item><see cref="Role.Assistant"/> indicates a message from the AI assistant/model</item>
    /// </list>
    /// The role is essential for maintaining proper turn-taking in conversations and ensuring appropriate
    /// model responses in sampling operations.
    /// </remarks>
    [JsonPropertyName("role")]
    public required Role Role { get; init; }  // "user" or "assistant"
}
