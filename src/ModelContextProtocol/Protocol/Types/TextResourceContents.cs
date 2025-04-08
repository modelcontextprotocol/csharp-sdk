using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents text-based contents of a resource in the Model Context Protocol.
/// Used for transmitting textual data like documents, source code, or other text content.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TextResourceContents"/> is used when textual data needs to be exchanged through
/// the Model Context Protocol. The text is stored directly in the <see cref="Text"/> property.
/// </para>
/// <para>
/// Common text-related MIME types include:
/// <list type="bullet">
///   <item><description>"text/plain" - For plain text content</description></item>
///   <item><description>"text/html" - For HTML content</description></item>
///   <item><description>"text/markdown" - For Markdown content</description></item>
///   <item><description>"text/csv" - For CSV data</description></item>
///   <item><description>"application/json" - For JSON data</description></item>
///   <item><description>"application/xml" - For XML data</description></item>
/// </list>
/// </para>
/// <para>
/// This class inherits from <see cref="ResourceContents"/>, which also has a sibling implementation
/// <see cref="BlobResourceContents"/> for binary resources. When working with resources, the
/// appropriate type is chosen based on the nature of the content.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema specification</see> for more details.
/// </para>
/// </remarks>
/// <example>
/// Creating and using a TextResourceContents:
/// <code>
/// // Create a TextResourceContents with text data
/// var resource = new TextResourceContents
/// {
///     Text = "This is some text content to be shared",
///     MimeType = "text/plain",
///     Uri = "resource://document.txt"
/// };
/// 
/// // Convert to AIContent for use with Microsoft.Extensions.AI
/// AIContent content = resource.ToAIContent();
/// </code>
/// </example>
/// <seealso cref="ResourceContents"/>
/// <seealso cref="BlobResourceContents"/>
/// <seealso cref="AIContentExtensions"/>
public class TextResourceContents : ResourceContents
{
    /// <summary>
    /// The text of the item. This must only be set if the item can actually be represented as text (not binary data).
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
