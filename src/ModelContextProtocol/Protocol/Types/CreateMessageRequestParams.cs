namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// A request from the server to sample an LLM via the client. 
/// The client has full discretion over which model to select. 
/// The client should also inform the user before beginning sampling, to allow them to inspect the request (human in the loop) and decide whether to approve it.
/// 
/// While these align with the protocol specification,
/// clients have full discretion over model selection and should inform users before sampling.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
/// <example>
/// <code>
/// // Create a request to sample an LLM
/// var requestParams = new CreateMessageRequestParams
/// {
///     Messages = 
///     [
///         new SamplingMessage
///         {
///             Role = Role.User,
///             Content = new Content { Type = "text", Text = "What is the capital of France?" }
///         }
///     ],
///     Temperature = 0.7f,
///     MaxTokens = 100,
///     StopSequences = ["END"],
///     SystemPrompt = "You are a helpful assistant."
/// };
/// 
/// // The request can then be used with RequestSamplingAsync
/// var result = await server.RequestSamplingAsync(requestParams, cancellationToken);
/// </code>
/// </example>
public class CreateMessageRequestParams : RequestParams
{
    /// <summary>
    /// Specifies which server contexts should be included in the prompt. The client MAY ignore this request.
    /// Options are:
    /// <list type="bullet">
    ///   <item><description><see cref="ContextInclusion.None"/>: No context will be included</description></item>
    ///   <item><description><see cref="ContextInclusion.ThisServer"/>: Include context only from the server sending this request</description></item>
    ///   <item><description><see cref="ContextInclusion.AllServers"/>: Include context from all servers the client is connected to</description></item>
    /// </list>
    /// </summary>
    /// <example>
    /// <code>
    /// var requestParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "How can I help you today?" }
    ///         }
    ///     ],
    ///     // Include context only from the server making this request
    ///     IncludeContext = ContextInclusion.ThisServer,
    ///     MaxTokens = 100
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("includeContext")]
    public ContextInclusion? IncludeContext { get; init; }

    /// <summary>
    /// The maximum number of tokens to generate in the LLM response, as requested by the server. 
    /// A token is generally a word or part of a word in the text. Setting this value helps control 
    /// response length and computation time. The client MAY choose to sample fewer tokens than requested.
    /// </summary>
    /// <example>
    /// <code>
    /// var requestParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "Tell me about Paris" }
    ///         }
    ///     ],
    ///     // Limit the response to 100 tokens
    ///     MaxTokens = 100,
    ///     Temperature = 0.7f
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Messages requested by the server to be included in the prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("messages")]
    public required IReadOnlyList<SamplingMessage> Messages { get; init; }

    /// <summary>
    /// Optional metadata to pass through to the LLM provider. The format of this metadata is provider-specific
    /// and can include model-specific settings or configuration that isn't covered by standard parameters.
    /// This allows for passing custom parameters that are specific to certain AI models or providers.
    /// </summary>
    /// <example>
    /// <code>
    /// var requestParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "Generate a short poem" }
    ///         }
    ///     ],
    ///     // Provider-specific metadata
    ///     Metadata = new Dictionary&lt;string, object&gt;
    ///     {
    ///         ["provider_model"] = "gpt-4",
    ///         ["custom_setting"] = "value",
    ///         ["frequency_penalty"] = 0.2,
    ///         ["presence_penalty"] = 0.5,
    ///         ["top_p"] = 0.95
    ///     }
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public object? Metadata { get; init; }

    /// <summary>
    /// The server's preferences for which model to select. The client MAY ignore these preferences.
    /// These preferences help the client make an appropriate model selection based on the server's priorities
    /// for cost, speed, intelligence, and specific model hints.
    /// </summary>
    /// <remarks>
    /// When multiple dimensions are specified (cost, speed, intelligence), the client should balance these
    /// based on their relative values. If specific model hints are provided, the client should evaluate them
    /// in order and prioritize them over numeric priorities.
    /// </remarks>
    /// <example>
    /// <code>
    /// var requestParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "Explain quantum computing" }
    ///         }
    ///     ],
    ///     // Set model preferences to prioritize intelligence over cost or speed
    ///     ModelPreferences = new ModelPreferences
    ///     {
    ///         IntelligencePriority = 0.9f,   // High priority on capabilities
    ///         CostPriority = 0.3f,           // Lower priority on cost
    ///         SpeedPriority = 0.2f,          // Speed is least important
    ///         Hints = new List&lt;ModelHint&gt;
    ///         {
    ///             new ModelHint { Name = "gpt-4" },         // Prefer this model family
    ///             new ModelHint { Name = "claude-3" }       // Fall back to this model family
    ///         }
    ///     },
    ///     MaxTokens = 1000
    /// };
    /// </code>
    /// </example>
    [System.Text.Json.Serialization.JsonPropertyName("modelPreferences")]
    public ModelPreferences? ModelPreferences { get; init; }

    /// <summary>
    /// Optional sequences of characters that signal the LLM to stop generating text when encountered.
    /// When the model generates any of these sequences during sampling, text generation stops immediately,
    /// even if the maximum token limit hasn't been reached. This is useful for controlling generation 
    /// endings or preventing the model from continuing beyond certain points.
    /// </summary>
    /// <example>
    /// <code>
    /// var requestParams = new CreateMessageRequestParams
    /// {
    ///     Messages = [
    ///         new SamplingMessage
    ///         {
    ///             Role = Role.User,
    ///             Content = new Content { Type = "text", Text = "Write a short story" }
    ///         }
    ///     ],
    ///     // Stop generation when "THE END" is encountered
    ///     StopSequences = ["THE END", "###"],
    ///     MaxTokens = 500
    /// };
    /// </code>
    /// </example>
    /// <remarks>
    /// Stop sequences are case-sensitive and the LLM will only stop generation when it exactly 
    /// matches one of the provided sequences. Common uses include ending markers like "END", punctuation
    /// like ".", or special delimiter sequences like "###".
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("stopSequences")]
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// An optional system prompt the server wants to use for sampling. The client MAY modify or omit this prompt.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// The temperature to use for sampling, as requested by the server.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("temperature")]
    public float? Temperature { get; init; }
}
