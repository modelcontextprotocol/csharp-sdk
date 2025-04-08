using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Abstract base class representing contents of a resource in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResourceContents"/> serves as the base class for different types of resources that can be 
/// exchanged through the Model Context Protocol. Resources are identified by URIs and can contain
/// different types of data.
/// </para>
/// <para>
/// This class is abstract and has two concrete implementations:
/// <list type="bullet">
///   <item><description><see cref="TextResourceContents"/> - For text-based resources</description></item>
///   <item><description><see cref="BlobResourceContents"/> - For binary data resources</description></item>
/// </list>
/// </para>
/// <para>
/// The JSON serialization of resources is handled by the <see cref="Converter"/> class, which
/// determines the appropriate concrete type based on the presence of either a "text" or "blob" field
/// in the JSON representation.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema specification</see> for more details.
/// </para>
/// </remarks>
/// <example>
/// Working with resources:
/// <code>
/// // Create a text resource
/// var textResource = new TextResourceContents
/// {
///     Uri = "resource://document.txt",
///     MimeType = "text/plain",
///     Text = "This is a text resource"
/// };
/// 
/// // Create a binary resource
/// var binaryResource = new BlobResourceContents
/// {
///     Uri = "resource://image.png",
///     MimeType = "image/png",
///     Blob = Convert.ToBase64String(imageBytes)
/// };
/// 
/// // Resources can be used with Content objects
/// var content = new Content
/// {
///     Type = "resource",
///     Resource = textResource
/// };
/// 
/// // Convert to AIContent for use with Microsoft.Extensions.AI
/// AIContent aiContent = textResource.ToAIContent();
/// </code>
/// </example>
/// <seealso cref="TextResourceContents"/>
/// <seealso cref="BlobResourceContents"/>
/// <seealso cref="Content"/>
/// <seealso cref="AIContentExtensions"/>
[JsonConverter(typeof(Converter))]
public abstract class ResourceContents
{
    internal ResourceContents()
    {
    }

    /// <summary>
    /// The URI of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// The MIME type of the resource content, specifying its format and how it should be interpreted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MIME types provide a standardized way to indicate the format of the resource content.
    /// This helps client applications properly interpret and display the data.
    /// </para>
    /// <para>
    /// Common MIME types include:
    /// <list type="bullet">
    ///   <item><description>"text/plain" for plain text content</description></item>
    ///   <item><description>"text/html" for HTML content</description></item>
    ///   <item><description>"image/png" for PNG images</description></item>
    ///   <item><description>"image/jpeg" for JPEG images</description></item>
    ///   <item><description>"application/pdf" for PDF documents</description></item>
    ///   <item><description>"application/json" for JSON data</description></item>
    ///   <item><description>"application/octet-stream" for generic binary data</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This property may be null if the MIME type is unknown, in which case clients may have to
    /// infer the type from the content or URI.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Text resource with MIME type
    /// var textResource = new TextResourceContents
    /// {
    ///     Uri = "resource://document.txt",
    ///     MimeType = "text/plain",
    ///     Text = "This is a text resource"
    /// };
    /// 
    /// // Binary resource with MIME type
    /// var imageResource = new BlobResourceContents
    /// {
    ///     Uri = "resource://image.png",
    ///     MimeType = "image/png",
    ///     Blob = Convert.ToBase64String(imageBytes)
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }


    /// <summary>
    /// Converter for <see cref="ResourceContents"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class Converter : JsonConverter<ResourceContents>
    {
        /// <inheritdoc/>
        public override ResourceContents? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            string? uri = null;
            string? mimeType = null;
            string? blob = null;
            string? text = null;

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
                    case "uri":
                        uri = reader.GetString();
                        break;
                    case "mimeType":
                        mimeType = reader.GetString();
                        break;
                    case "blob":
                        blob = reader.GetString();
                        break;
                    case "text":
                        text = reader.GetString();
                        break;
                    default:
                        break;
                }
            }

            if (blob is not null)
            {
                return new BlobResourceContents
                {
                    Uri = uri ?? string.Empty,
                    MimeType = mimeType,
                    Blob = blob
                };
            }

            if (text is not null)
            {
                return new TextResourceContents
                {
                    Uri = uri ?? string.Empty,
                    MimeType = mimeType,
                    Text = text
                };
            }

            return null;
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ResourceContents value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("uri", value.Uri);
            writer.WriteString("mimeType", value.MimeType);
            Debug.Assert(value is BlobResourceContents or TextResourceContents);
            if (value is BlobResourceContents blobResource)
            {
                writer.WriteString("blob", blobResource.Blob);
            }
            else if (value is TextResourceContents textResource)
            {
                writer.WriteString("text", textResource.Text);
            }
            writer.WriteEndObject();
        }
    }
}
