using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents content within the Model Context Protocol (MCP) that can contain text, binary data, or references to resources.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Content"/> class is a fundamental type in the MCP that can represent different forms of content
/// based on the <see cref="Type"/> property. The main content types are:
/// </para>
/// <list type="bullet">
///   <item><description>"text" - Textual content, stored in the <see cref="Text"/> property</description></item>
///   <item><description>"image" - Image data, stored as base64 in the <see cref="Data"/> property with appropriate MIME type</description></item>
///   <item><description>"audio" - Audio data, stored as base64 in the <see cref="Data"/> property with appropriate MIME type</description></item>
///   <item><description>"resource" - Reference to a resource, accessed through the <see cref="Resource"/> property</description></item>
/// </list>
/// <para>
/// This class is used extensively throughout the MCP for representing content in messages, tool responses,
/// and other communication between clients and servers.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema specification</see> for more details.
/// </para>
/// </remarks>
/// <example>
/// Creating and using different types of Content:
/// <code>
/// // Text content
/// var textContent = new Content
/// {
///     Type = "text",
///     Text = "Hello, this is a text message."
/// };
/// 
/// // Image content
/// var imageContent = new Content
/// {
///     Type = "image",
///     Data = Convert.ToBase64String(imageBytes),
///     MimeType = "image/png"
/// };
/// 
/// // Resource content
/// var resourceContent = new Content
/// {
///     Type = "resource",
///     Resource = new TextResourceContents
///     {
///         Uri = "resource://text.txt",
///         Text = "This is a resource text."
///     }
/// };
/// 
/// // Convert to AIContent for use with Microsoft.Extensions.AI
/// AIContent aiContent = textContent.ToAIContent();
/// </code>
/// </example>
/// <seealso cref="ResourceContents"/>
/// <seealso cref="BlobResourceContents"/>
/// <seealso cref="TextResourceContents"/>
/// <seealso cref="AIContentExtensions.ToAIContent(Content)"/>
public class Content
{
    /// <summary>
    /// The type of content. This determines the structure of the content object. Can be "image", "audio", "text", "resource".
    /// </summary>

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// The base64-encoded image data.
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// The MIME type of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used when <see cref="Type"/> is "image" or "audio" to indicate the specific format of the binary data.
    /// Common values include "image/png", "image/jpeg", "audio/wav", "audio/mp3", etc.
    /// </para>
    /// <para>
    /// This property is typically required when the <see cref="Data"/> property contains binary content,
    /// as it helps clients properly interpret and render the content.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Image content with MIME type
    /// var imageContent = new Content
    /// {
    ///     Type = "image",
    ///     Data = Convert.ToBase64String(imageBytes),
    ///     MimeType = "image/png"
    /// };
    /// 
    /// // Audio content with MIME type
    /// var audioContent = new Content
    /// {
    ///     Type = "audio",
    ///     Data = Convert.ToBase64String(audioBytes),
    ///     MimeType = "audio/wav"
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// The resource content of the message when <see cref="Type"/> is "resource".
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is used to embed or reference resource data within a message. It's only 
    /// applicable when the <see cref="Type"/> property is set to "resource".
    /// </para>
    /// <para>
    /// Resources can be either text-based (<see cref="TextResourceContents"/>) or 
    /// binary (<see cref="BlobResourceContents"/>), allowing for flexible data representation.
    /// Each resource has a URI that can be used for identification and retrieval.
    /// </para>
    /// <para>
    /// When converting to <see cref="Microsoft.Extensions.AI.AIContent"/> using 
    /// <see cref="AIContentExtensions.ToAIContent(Content)"/>, the appropriate content type 
    /// will be created based on the resource type.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a Content object with a text resource
    /// var content = new Content
    /// {
    ///     Type = "resource",
    ///     Resource = new TextResourceContents
    ///     {
    ///         Uri = "resource://document.txt",
    ///         MimeType = "text/plain",
    ///         Text = "This is a text resource"
    ///     }
    /// };
    /// 
    /// // Create a Content object with a binary resource
    /// var imageContent = new Content
    /// {
    ///     Type = "resource",
    ///     Resource = new BlobResourceContents
    ///     {
    ///         Uri = "resource://image.png",
    ///         MimeType = "image/png",
    ///         Blob = Convert.ToBase64String(imageBytes)
    ///     }
    /// };
    /// </code>
    /// </example>
    /// <seealso cref="ResourceContents"/>
    /// <seealso cref="TextResourceContents"/>
    /// <seealso cref="BlobResourceContents"/>
    /// <seealso cref="AIContentExtensions.ToAIContent(Content)"/>
    [JsonPropertyName("resource")]
    public ResourceContents? Resource { get; set; }

    /// <summary>
    /// Optional annotations for the content.
    /// These annotations can be used to specify the intended audience (User, Assistant, or both)
    /// and the priority level of the content. Clients can use this information to filter
    /// or prioritize content for different roles.
    /// </summary>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}