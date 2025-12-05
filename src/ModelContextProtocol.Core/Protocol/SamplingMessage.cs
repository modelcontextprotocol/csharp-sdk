using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a message issued to or received from an LLM API within the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SamplingMessage"/> encapsulates content sent to or received from AI models in the Model Context Protocol.
/// The message has a role (<see cref="Role.User"/> or <see cref="Role.Assistant"/>) and content which can be text, images,
/// audio, tool uses, or tool results.
/// </para>
/// <para>
/// <see cref="SamplingMessage"/> objects are typically used in collections within <see cref="CreateMessageRequestParams"/>
/// to represent prompts or queries for LLM sampling. They form the core data structure for text generation requests
/// within the Model Context Protocol.
/// </para>
/// <para>
/// If content contains any <see cref="ToolResultContentBlock"/>, then all content items
/// must be <see cref="ToolResultContentBlock"/>. Tool results cannot be mixed with text, image, or
/// audio content in the same message.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SamplingMessage
{
    /// <summary>
    /// Gets or sets the content of the message.
    /// </summary>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(SingleItemOrListConverter<ContentBlock>))]
    public required IList<ContentBlock> Content { get; set; }

    /// <summary>
    /// Gets or sets the role of the message sender.
    /// </summary>
    [JsonPropertyName("role")]
    public Role Role { get; set; } = Role.User;

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            // Show actual text content if it's a single TextContentBlock
            if (Content.Count == 1 && Content[0] is TextContentBlock textBlock)
            {
                return $"Role = {Role}, Text = \"{textBlock.Text}\"";
            }

            string contentTypes = Content.Count == 1 
                ? Content[0].Type 
                : $"{Content.Count} items";
            return $"Role = {Role}, Content = {contentTypes}";
        }
    }
}
