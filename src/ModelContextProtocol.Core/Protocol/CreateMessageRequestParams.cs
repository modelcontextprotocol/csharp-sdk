using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.SamplingCreateMessage"/>
/// request from a server to sample an LLM via the client.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class CreateMessageRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets an indication as to which server contexts should be included in the prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client might ignore this request.
    /// </para>
    /// <para>
    /// <see cref="ContextInclusion"/>, and in particular <see cref="ContextInclusion.ThisServer"/> and
    /// <see cref="ContextInclusion.AllServers"/>, are deprecated. Servers should only use these values if the client
    /// declares <see cref="ClientCapabilities.Sampling"/> with <see cref="SamplingCapability.Context"/> set.
    /// These values might be removed in future spec releases.
    /// </para>
    /// </remarks>
    [JsonPropertyName("includeContext")]
    public ContextInclusion? IncludeContext { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate in the LLM response, as requested by the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A token is generally a word or part of a word in the text. Setting this value helps control
    /// response length and computation time. The client can choose to sample fewer tokens than requested.
    /// </para>
    /// <para>
    /// The client must respect the <see cref="MaxTokens"/> parameter.
    /// </para>
    /// </remarks>
    [JsonPropertyName("maxTokens")]
    public required int MaxTokens { get; set; }

    /// <summary>
    /// Gets or sets the messages requested by the server to be included in the prompt.
    /// </summary>
    /// <remarks>
    /// The list of messages in a sampling request should not be retained between separate requests.
    /// </remarks>
    [JsonPropertyName("messages")]
    public IList<SamplingMessage> Messages { get; set; } = [];

    /// <summary>
    /// Gets or sets optional metadata to pass through to the LLM provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The format of this metadata is provider-specific and can include model-specific settings or
    /// configuration that isn't covered by standard parameters. This allows for passing custom parameters
    /// that are specific to certain AI models or providers.
    /// </para>
    /// <para>
    /// The client may modify or ignore metadata.
    /// </para>
    /// </remarks>
    [JsonPropertyName("metadata")]
    public JsonObject? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the server's preferences for which model to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client might ignore these preferences.
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
    public ModelPreferences? ModelPreferences { get; set; }

    /// <summary>
    /// Gets or sets optional sequences of characters that signal the LLM to stop generating text when encountered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the model generates any of these sequences during sampling, text generation stops immediately,
    /// even if the maximum token limit hasn't been reached. This behavior is useful for controlling generation
    /// endings or preventing the model from continuing beyond certain points.
    /// </para>
    /// <para>
    /// Stop sequences are typically case-sensitive, and the LLM will only stop generation when a produced
    /// sequence exactly matches one of the provided sequences. Common uses include ending markers like "END", punctuation
    /// like ".", or special delimiter sequences like "###".
    /// </para>
    /// <para>
    /// The client may modify or ignore stop sequences.
    /// </para>
    /// </remarks>
    [JsonPropertyName("stopSequences")]
    public IList<string>? StopSequences { get; set; }

    /// <summary>
    /// Gets or sets an optional system prompt the server wants to use for sampling.
    /// </summary>
    /// <remarks>
    /// The client might modify or omit this prompt.
    /// </remarks>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Gets or sets the temperature to use for sampling, as requested by the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Temperature controls randomness in model responses. Higher values produce higher randomness,
    /// and lower values produce more stable output. The valid range depends on the model provider.
    /// </para>
    /// <para>
    /// The client may modify or ignore this value.
    /// </para>
    /// </remarks>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Gets or sets tools that the model can use during generation.
    /// </summary>
    /// <remarks>
    /// The tool definitions in this array are scoped to this sampling request.
    /// They do not need to correspond to tools registered on the server via <see cref="RequestMethods.ToolsList"/>.
    /// </remarks>
    [JsonPropertyName("tools")]
    public IList<Tool>? Tools { get; set; }

    /// <summary>
    /// Gets or sets controls for how the model uses tools.
    /// </summary>
    /// <remarks>
    /// This controls whether and how the model uses the request-scoped <see cref="Tools"/> during sampling.
    /// </remarks>
    [JsonPropertyName("toolChoice")]
    public ToolChoice? ToolChoice { get; set; }

    /// <summary>
    /// Gets or sets optional task metadata to augment this request with task execution.
    /// </summary>
    /// <remarks>
    /// When present, indicates that the requestor wants this operation executed as a task.
    /// The receiver must support task augmentation for this specific request type.
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    [JsonIgnore]
    public McpTaskMetadata? Task
    {
        get => TaskCore;
        set => TaskCore = value;
    }

    // See ExperimentalInternalPropertyTests.cs before modifying this property.
    [JsonInclude]
    [JsonPropertyName("task")]
    internal McpTaskMetadata? TaskCore { get; set; }
}
