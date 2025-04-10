using ModelContextProtocol.Protocol.Messages;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.SamplingCreateMessage"/> 
/// request from a server to sample an LLM via the client.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public class CreateMessageRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets an indication as to which server contexts should be included in the prompt.
    /// </summary>
    /// <remarks>
    /// The client may ignore this request.
    /// </remarks>
    [JsonPropertyName("includeContext")]
    public ContextInclusion? IncludeContext { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate in the LLM response, as requested by the server.
    /// </summary>
    /// <remarks>
    /// A token is generally a word or part of a word in the text. Setting this value helps control 
    /// response length and computation time. The client may choose to sample fewer tokens than requested.
    /// </remarks>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets or sets the messages requested by the server to be included in the prompt.
    /// </summary>
    [JsonPropertyName("messages")]
    public required IReadOnlyList<SamplingMessage> Messages { get; init; }

    /// <summary>
    /// Gets or sets optional metadata to pass through to the LLM provider.
    /// </summary>
    /// <remarks>
    /// The format of this metadata is provider-specific and can include model-specific settings or
    /// configuration that isn't covered by standard parameters. This allows for passing custom parameters 
    /// that are specific to certain AI models or providers.
    /// </remarks>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; init; }

    /// <summary>
    /// Gets or sets the server's preferences for which model to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client may ignore these preferences.
    /// </para>
    /// <para>
    /// These preferences help the client make an appropriate model selection based on the server's priorities
    /// for cost, speed, intelligence, and specific model hints.
    /// </para>
    /// <para>
    /// When multiple dimensions are specified (cost, speed, intelligence), the client should balance these
    /// based on their relative values. If specific model hints are provided, the client should evaluate them
    /// in order and prioritize them over numeric priorities.
    /// </para>
    /// </remarks>
    [JsonPropertyName("modelPreferences")]
    public ModelPreferences? ModelPreferences { get; init; }

    /// <summary>
    /// Gets or sets optional sequences of characters that signal the LLM to stop generating text when encountered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the model generates any of these sequences during sampling, text generation stops immediately,
    /// even if the maximum token limit hasn't been reached. This is useful for controlling generation 
    /// endings or preventing the model from continuing beyond certain points.
    /// </para>
    /// <para>
    /// Stop sequences are typically case-sensitive, and typically the LLM will only stop generation when a produced
    /// sequence exactly matches one of the provided sequences. Common uses include ending markers like "END", punctuation
    /// like ".", or special delimiter sequences like "###".
    /// </para>
    /// </remarks>
    [JsonPropertyName("stopSequences")]
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Gets or sets an optional system prompt the server wants to use for sampling.
    /// </summary>
    /// <remarks>
    /// The client may modify or omit this prompt.
    /// </remarks>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// Gets or sets the temperature to use for sampling, as requested by the server.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; init; }
}
