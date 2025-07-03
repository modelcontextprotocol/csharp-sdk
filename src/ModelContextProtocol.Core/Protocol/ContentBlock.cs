using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents content within the Model Context Protocol (MCP).
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ContentBlock"/> class is a fundamental type in the MCP that can represent different forms of content
/// based on the <see cref="Type"/> property. Derived types like <see cref="TextContentBlock"/>, <see cref="ImageContentBlock"/>,
/// and <see cref="EmbeddedResourceBlock"/> provide the type-specific content.
/// </para>
/// <para>
/// This class is used throughout the MCP for representing content in messages, tool responses,
/// and other communication between clients and servers.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))] // TODO: This converter exists due to the lack of downlevel support for AllowOutOfOrderMetadataProperties.
public abstract class ContentBlock
{
    /// <summary>Prevent external derivations.</summary>
    private protected ContentBlock()
    {
    }

    /// <summary>
    /// Gets or sets the type of content.
    /// </summary>
    /// <remarks>
    /// This determines the structure of the content object. Valid values include "image", "audio", "text", "resource", and "resource_link".
    /// </remarks>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional annotations for the content.
    /// </summary>
    /// <remarks>
    /// These annotations can be used to specify the intended audience (<see cref="Role.User"/>, <see cref="Role.Assistant"/>, or both)
    /// and the priority level of the content. Clients can use this information to filter or prioritize content for different roles.
    /// </remarks>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; init; }

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="ContentBlock"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class Converter : JsonConverter<ContentBlock>
    {
        /// <inheritdoc/>
        public override ContentBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            string? type = null;
            string? text = null;
            string? name = null;
            string? data = null;
            string? mimeType = null;
            string? uri = null;
            string? description = null;
            long? size = null;
            ResourceContents? resource = null;
            Annotations? annotations = null;
            JsonObject? meta = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                string? propertyName = reader.GetString();
                bool success = reader.Read();
                Debug.Assert(success, "STJ must have buffered the entire object for us.");

                switch (propertyName)
                {
                    case "type":
                        type = reader.GetString();
                        break;

                    case "text":
                        text = reader.GetString();
                        break;

                    case "data":
                        data = reader.GetString();
                        break;

                    case "mimeType":
                        mimeType = reader.GetString();
                        break;

                    case "uri":
                        uri = reader.GetString();
                        break;

                    case "description":
                        description = reader.GetString();
                        break;

                    case "size":
                        size = reader.GetInt64();
                        break;

                    case "resource":
                        resource = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.ResourceContents);
                        break;

                    case "annotations":
                        annotations = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.Annotations);
                        break;

                    case "_meta":
                        meta = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.JsonObject);
                        break;

                    default:
                        break;
                }
            }

            return type switch
            {
                "text" => new TextContentBlock
                {
                    Text = text ?? throw new JsonException("Text contents must be provided for 'text' type."),
                    Annotations = annotations,
                    Meta = meta,
                },

                "image" => new ImageContentBlock
                {
                    Data = data ?? throw new JsonException("Image data must be provided for 'image' type."),
                    MimeType = mimeType ?? throw new JsonException("MIME type must be provided for 'image' type."),
                    Annotations = annotations,
                    Meta = meta,
                },

                "audio" => new AudioContentBlock
                {
                    Data = data ?? throw new JsonException("Audio data must be provided for 'audio' type."),
                    MimeType = mimeType ?? throw new JsonException("MIME type must be provided for 'audio' type."),
                    Annotations = annotations,
                    Meta = meta,
                },

                "resource" => new EmbeddedResourceBlock
                {
                    Resource = resource ?? throw new JsonException("Resource contents must be provided for 'resource' type."),
                    Annotations = annotations,
                    Meta = meta,
                },

                "resource_link" => new ResourceLinkBlock
                {
                    Uri = uri ?? throw new JsonException("URI must be provided for 'resource_link' type."),
                    Name = name ?? throw new JsonException("Name must be provided for 'resource_link' type."),
                    Description = description,
                    MimeType = mimeType,
                    Size = size,
                    Annotations = annotations,
                },

                _ => throw new JsonException($"Unknown content type: '{type}'"),
            };
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ContentBlock value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();

            writer.WriteString("type", value.Type);

            switch (value)
            {
                case TextContentBlock textContent:
                    writer.WriteString("text", textContent.Text);
                    if (textContent.Meta is not null)
                    {
                        writer.WritePropertyName("_meta");
                        JsonSerializer.Serialize(writer, textContent.Meta, McpJsonUtilities.JsonContext.Default.JsonObject);
                    }
                    break;

                case ImageContentBlock imageContent:
                    writer.WriteString("data", imageContent.Data);
                    writer.WriteString("mimeType", imageContent.MimeType);
                    if (imageContent.Meta is not null)
                    {
                        writer.WritePropertyName("_meta");
                        JsonSerializer.Serialize(writer, imageContent.Meta, McpJsonUtilities.JsonContext.Default.JsonObject);
                    }
                    break;

                case AudioContentBlock audioContent:
                    writer.WriteString("data", audioContent.Data);
                    writer.WriteString("mimeType", audioContent.MimeType);
                    if (audioContent.Meta is not null)
                    {
                        writer.WritePropertyName("_meta");
                        JsonSerializer.Serialize(writer, audioContent.Meta, McpJsonUtilities.JsonContext.Default.JsonObject);
                    }
                    break;

                case EmbeddedResourceBlock embeddedResource:
                    writer.WritePropertyName("resource");
                    JsonSerializer.Serialize(writer, embeddedResource.Resource, McpJsonUtilities.JsonContext.Default.ResourceContents);
                    if (embeddedResource.Meta is not null)
                    {
                        writer.WritePropertyName("_meta");
                        JsonSerializer.Serialize(writer, embeddedResource.Meta, McpJsonUtilities.JsonContext.Default.JsonObject);
                    }
                    break;

                case ResourceLinkBlock resourceLink:
                    writer.WriteString("uri", resourceLink.Uri);
                    writer.WriteString("name", resourceLink.Name);
                    if (resourceLink.Description is not null)
                    {
                        writer.WriteString("description", resourceLink.Description);
                    }
                    if (resourceLink.MimeType is not null)
                    {
                        writer.WriteString("mimeType", resourceLink.MimeType);
                    }
                    if (resourceLink.Size.HasValue)
                    {
                        writer.WriteNumber("size", resourceLink.Size.Value);
                    }
                    break;
            }

            if (value.Annotations is { } annotations)
            {
                writer.WritePropertyName("annotations");
                JsonSerializer.Serialize(writer, annotations, McpJsonUtilities.JsonContext.Default.Annotations);
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>Represents text provided to or from an LLM.</summary>
public sealed class TextContentBlock : ContentBlock
{
    /// <summary>Initializes the instance of the <see cref="TextContentBlock"/> class.</summary>
    public TextContentBlock() => Type = "text";

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }
}

/// <summary>Represents an image provided to or from an LLM.</summary>
public sealed class ImageContentBlock : ContentBlock
{
    /// <summary>Initializes the instance of the <see cref="ImageContentBlock"/> class.</summary>
    public ImageContentBlock() => Type = "image";

    /// <summary>
    /// Gets or sets the base64-encoded image data.
    /// </summary>
    [JsonPropertyName("data")]
    public required string Data { get; set; }

    /// <summary>
    /// Gets or sets the MIME type (or "media type") of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common values include "image/png" and "image/jpeg".
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }
}

/// <summary>Represents audio provided to or from an LLM.</summary>
public sealed class AudioContentBlock : ContentBlock
{
    /// <summary>Initializes the instance of the <see cref="AudioContentBlock"/> class.</summary>
    public AudioContentBlock() => Type = "audio";

    /// <summary>
    /// Gets or sets the base64-encoded audio data.
    /// </summary>
    [JsonPropertyName("data")]
    public required string Data { get; set; }

    /// <summary>
    /// Gets or sets the MIME type (or "media type") of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common values include "audio/wav" and "audio/mp3".
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }
}

/// <summary>Represents the contents of a resource, embedded into a prompt or tool call result.</summary>
/// <remarks>
/// It is up to the client how best to render embedded resources for the benefit of the LLM and/or the user.
/// </remarks>
public sealed class EmbeddedResourceBlock : ContentBlock
{
    /// <summary>Initializes the instance of the <see cref="ResourceLinkBlock"/> class.</summary>
    public EmbeddedResourceBlock() => Type = "resource";

    /// <summary>
    /// Gets or sets the resource content of the message when <see cref="Type"/> is "resource".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resources can be either text-based (<see cref="TextResourceContents"/>) or 
    /// binary (<see cref="BlobResourceContents"/>), allowing for flexible data representation.
    /// Each resource has a URI that can be used for identification and retrieval.
    /// </para>
    /// </remarks>
    [JsonPropertyName("resource")]
    public required ResourceContents Resource { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }
}

/// <summary>Represents a resource that the server is capable of reading, included in a prompt or tool call result.</summary>
/// <remarks>
/// Resource links returned by tools are not guaranteed to appear in the results of `resources/list` requests.
/// </remarks>
public sealed class ResourceLinkBlock : ContentBlock
{
    /// <summary>Initializes the instance of the <see cref="ResourceLinkBlock"/> class.</summary>
    public ResourceLinkBlock() => Type = "resource_link";

    /// <summary>
    /// Gets or sets the URI of this resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>
    /// Gets or sets a human-readable name for this resource.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets a description of what this resource represents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This can be used by clients to improve the LLM's understanding of available resources. It can be thought of like a \"hint\" to the model.
    /// </para>
    /// <para>
    /// The description should provide clear context about the resource's content, format, and purpose.
    /// This helps AI models make better decisions about when to access or reference the resource.
    /// </para>
    /// <para>
    /// Client applications can also use this description for display purposes in user interfaces
    /// or to help users understand the available resources.
    /// </para>
    /// </remarks>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the MIME type of this resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MimeType"/> specifies the format of the resource content, helping clients to properly interpret and display the data.
    /// Common MIME types include "text/plain" for plain text, "application/pdf" for PDF documents,
    /// "image/png" for PNG images, and "application/json" for JSON data.
    /// </para>
    /// <para>
    /// This property may be <see langword="null"/> if the MIME type is unknown or not applicable for the resource.
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    /// <summary>
    /// Gets or sets the size of the raw resource content (before base64 encoding), in bytes, if known.
    /// </summary>
    /// <remarks>
    /// This can be used by applications to display file sizes and estimate context window usage.
    /// </remarks>
    [JsonPropertyName("size")]
    public long? Size { get; init; }
}