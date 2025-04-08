using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The client's response to a sampling/create_message request from the server. 
/// The client should inform the user before returning the sampled message, to allow them to inspect the response (human in the loop) 
/// and decide whether to allow the server to see it.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <example>
/// <code>
/// // Example of creating a response to a sampling request
/// var result = new CreateMessageResult
/// {
///     Content = new Content { Type = "text", Text = "Paris is the capital of France." },
///     Model = "gpt-4",
///     Role = "assistant",
///     StopReason = "endTurn"
/// };
/// </code>
/// </example>
public class CreateMessageResult
{
    /// <summary>
    /// Text or image content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public required Content Content { get; init; }

    /// <summary>
    /// The name of the model that generated the message. This should contain the
    /// specific model identifier such as "claude-3-5-sonnet-20241022" or "gpt-4".
    /// </summary>
    /// <remarks>
    /// This property allows the server to know which model was used to generate the response,
    /// enabling appropriate handling based on the model's capabilities and characteristics.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = new CreateMessageResult
    /// {
    ///     Content = new Content { Type = "text", Text = "Paris is the capital of France." },
    ///     Model = "claude-3-5-sonnet-20241022",
    ///     Role = "assistant"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// The reason why message generation (sampling) stopped, if known.
    /// </summary>
    /// <remarks>
    /// Common values include:
    /// <list type="bullet">
    ///   <item><term>endTurn</term><description>The model naturally completed its response.</description></item>
    ///   <item><term>maxTokens</term><description>The response was truncated due to reaching token limits.</description></item>
    ///   <item><term>stopSequence</term><description>A specific stop sequence was encountered during generation.</description></item>
    /// </list>
    /// This property is mapped to model-specific finish reasons when translating between protocols.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = new CreateMessageResult
    /// {
    ///     Content = new Content { Type = "text", Text = "The answer is..." },
    ///     Model = "gpt-4",
    ///     Role = "assistant",
    ///     StopReason = "endTurn"  // Normal completion
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }

    /// <summary>
    /// The role of the user who generated the message.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}
