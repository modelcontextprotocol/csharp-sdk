using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a prompt that the server offers.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </remarks>
public sealed class Prompt : IBaseMetadata
{
    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets an optional description of what this prompt provides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description helps developers understand the purpose and use cases for the prompt.
    /// It should explain what the prompt is designed to accomplish and any important context.
    /// </para>
    /// <para>
    /// The description is typically used in documentation, UI displays, and for providing context
    /// to client applications that may need to choose between multiple available prompts.
    /// </para>
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a list of arguments that this prompt accepts for templating and customization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list defines the arguments that can be provided when requesting the prompt.
    /// Each argument specifies metadata like name, description, and whether it's required.
    /// </para>
    /// <para>
    /// When a client makes a <see cref="RequestMethods.PromptsGet"/> request, it can provide values for these arguments
    /// which will be substituted into the prompt template or otherwise used to render the prompt.
    /// </para>
    /// </remarks>
    [JsonPropertyName("arguments")]
    public IList<PromptArgument>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets an optional list of icons for this prompt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be used by clients to display the prompt's icon in a user interface.
    /// Multiple icons can be provided to support different display contexts and resolutions.
    /// Clients should select the most appropriate icon based on their UI requirements.
    /// </para>
    /// <para>
    /// Each icon should specify a source URI that points to the icon file or data representation, 
    /// and may also include MIME type and size information to help clients choose the best icon.
    /// </para>
    /// </remarks>
    [JsonPropertyName("icons")]
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets the callable server prompt corresponding to this metadata if any.
    /// </summary>
    [JsonIgnore]
    public McpServerPrompt? McpServerPrompt { get; set; }
}
