using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a client's response to a <see cref="RequestMethods.SamplingCreateMessage"/> from the server.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class CreateMessageResult : Result
{
    /// <summary>
    /// Gets or sets the content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    public required ContentBlock Content { get; init; }

    /// <summary>
    /// Gets or sets the name of the model that generated the message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This should contain the specific model identifier such as "claude-3-5-sonnet-20241022" or "o3-mini".
    /// </para>
    /// <para>
    /// This property allows the server to know which model was used to generate the response,
    /// enabling appropriate handling based on the model's capabilities and characteristics.
    /// </para>
    /// </remarks>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// Gets or sets the reason why message generation (sampling) stopped, if known.
    /// </summary>
    /// <remarks>
    /// Common values include:
    /// <list type="bullet">
    ///   <item><term>endTurn</term><description>The model naturally completed its response.</description></item>
    ///   <item><term>maxTokens</term><description>The response was truncated due to reaching token limits.</description></item>
    ///   <item><term>stopSequence</term><description>A specific stop sequence was encountered during generation.</description></item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }

    /// <summary>
    /// Gets or sets the role of the user who generated the message.
    /// </summary>
    [JsonPropertyName("role")]
    public required Role Role { get; init; }
}
