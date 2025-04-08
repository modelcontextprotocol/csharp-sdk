using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents binary contents of a resource in the Model Context Protocol.
/// Used for transmitting binary data like images, audio, or other non-text content.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlobResourceContents"/> is used when binary data needs to be exchanged through
/// the Model Context Protocol. The binary data is represented as a base64-encoded string
/// in the <see cref="Blob"/> property.
/// </para>
/// <para>
/// This class inherits from <see cref="ResourceContents"/>, which also has a sibling implementation
/// <see cref="TextResourceContents"/> for text-based resources. When working with resources, the
/// appropriate type is chosen based on the nature of the content.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema specification</see> for more details.
/// </para>
/// </remarks>
/// <example>
/// Creating and using a BlobResourceContents:
/// <code>
/// // Create a BlobResourceContents with binary data
/// var imageBytes = File.ReadAllBytes("image.png");
/// var resource = new BlobResourceContents
/// {
///     Blob = Convert.ToBase64String(imageBytes),
///     MimeType = "image/png",
///     Uri = "resource://image.png"
/// };
/// 
/// // Convert to AIContent for use with Microsoft.Extensions.AI
/// AIContent content = resource.ToAIContent();
/// </code>
/// </example>
/// <seealso cref="ResourceContents"/>
/// <seealso cref="TextResourceContents"/>
/// <seealso cref="AIContentExtensions"/>
public class BlobResourceContents : ResourceContents
{
    /// <summary>
    /// The base64-encoded string representing the binary data of the item.
    /// </summary>
    [JsonPropertyName("blob")]
    public string Blob { get; set; } = string.Empty;
}