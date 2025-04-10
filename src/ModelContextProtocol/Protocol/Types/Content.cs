using Microsoft.Extensions.AI;
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
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
public class Content
{
    /// <summary>
    /// Gets or sets the type of content.
    /// </summary>
    /// <remarks>
    /// This determines the structure of the content object. Valid values include "image", "audio", "text", and "resource".
    /// </remarks>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>
    /// Gets or sets the base64-encoded image or audio data.
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// Gets or sets the MIME type (or "media type") of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is used when <see cref="Type"/> is "image", "audio", or "resource", to indicate the specific format of the binary data.
    /// Common values include "image/png", "image/jpeg", "audio/wav", and "audio/mp3".
    /// </para>
    /// <para>
    /// This property is required when the <see cref="Data"/> property contains binary content,
    /// as it helps clients properly interpret and render the content.
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the resource content of the message when <see cref="Type"/> is "resource".
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
    /// </remarks>
    [JsonPropertyName("resource")]
    public ResourceContents? Resource { get; set; }

    /// <summary>
    /// Gets or sets optional annotations for the content.
    /// </summary>
    /// <remarks>
    /// These annotations can be used to specify the intended audience (<see cref="Role.User"/>, <see cref="Role.Assistant"/>, or both)
    /// and the priority level of the content. Clients can use this information to filter or prioritize content for different roles.
    /// </remarks>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }
}