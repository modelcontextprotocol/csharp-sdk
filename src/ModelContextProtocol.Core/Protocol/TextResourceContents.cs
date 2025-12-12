using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents text-based contents of a resource in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TextResourceContents"/> is used when textual data needs to be exchanged through
/// the Model Context Protocol. The text is stored directly in the <see cref="Text"/> property.
/// </para>
/// <para>
/// This class inherits from <see cref="ResourceContents"/>, which also has a sibling implementation
/// <see cref="BlobResourceContents"/> for binary resources. When working with resources, the
/// appropriate type is chosen based on the nature of the content.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
[DebuggerDisplay("Uri = \"{Uri}\", Text = \"{Text}\"")]
public sealed class TextResourceContents : ResourceContents
{
    /// <summary>
    /// Gets or sets the text of the item.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Text ?? "";
}
