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
    /// Gets or sets the content of the assistant's response.
    /// </summary>
    /// <remarks>
    /// In the corresponding JSON, this might be a single content block or an array of content blocks.
    /// </remarks>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(SingleItemOrListConverter<ContentBlock>))]
    public required IList<ContentBlock> Content { get; set; }

    /// <summary>
    /// Gets or sets the name of the model that generated the message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value should contain the specific model identifier such as "claude-3-5-sonnet-20241022" or "o3-mini".
    /// </para>
    /// <para>
    /// This property allows the server to know which model was used to generate the response,
    /// enabling appropriate handling based on the model's capabilities and characteristics.
    /// </para>
    /// </remarks>
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    /// <summary>
    /// Gets or sets the reason why message generation (sampling) stopped, if known.
    /// </summary>
    /// <remarks>
    /// Standard values include:
    /// <list type="bullet">
    ///   <item><term>endTurn</term><description>The participant is yielding the conversation to the other party.</description></item>
    ///   <item><term>maxTokens</term><description>The response was truncated due to reaching token limits.</description></item>
    ///   <item><term>stopSequence</term><description>A specific stop sequence was encountered during generation.</description></item>
    ///   <item><term>toolUse</term><description>The model wants to use one or more tools.</description></item>
    /// </list>
    /// This field is an open string to allow for provider-specific stop reasons.
    /// </remarks>
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; set; }

    /// <summary>
    /// Gets or sets the role of the user who generated the message.
    /// </summary>
    /// <value>
    /// The role of the user who generated the message. The default is <see cref="Role.Assistant"/>.
    /// </value>
    [JsonPropertyName("role")]
    public Role Role { get; set; } = Role.Assistant;

    /// <summary>The stop reason "endTurn".</summary>
    internal const string StopReasonEndTurn = "endTurn";

    /// <summary>The stop reason "maxTokens".</summary>
    internal const string StopReasonMaxTokens = "maxTokens";

    /// <summary>The stop reason "stopSequence".</summary>
    internal const string StopReasonStopSequence = "stopSequence";

    /// <summary>The stop reason "toolUse".</summary>
    internal const string StopReasonToolUse = "toolUse";
}
