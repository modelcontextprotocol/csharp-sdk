using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class representing contents of a resource in the Model Context Protocol.
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
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class ResourceContents
{
    /// <summary>Prevent external derivations.</summary>
    private protected ResourceContents()
    {
    }

    /// <summary>
    /// Gets or sets the URI of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the resource content.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="ResourceContents"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ResourceContents>
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
            ReadOnlyMemory<byte>? blob = null;
            ReadOnlyMemory<byte>? decodedBlob = null;
            string? text = null;
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
                    case "uri":
                        uri = reader.GetString();
                        break;

                    case "mimeType":
                        mimeType = reader.GetString();
                        break;

                    case "blob":
                        if (!reader.ValueIsEscaped)
                        {
                            blob = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                        }
                        else
                        {
                            decodedBlob = reader.GetBytesFromBase64();
                        }
                        break;

                    case "text":
                        text = reader.GetString();
                        break;

                    case "_meta":
                        meta = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.JsonObject);
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }

            if (decodedBlob is not null)
            {
                var blobResource = BlobResourceContents.FromBytes(decodedBlob.Value, uri ?? string.Empty, mimeType);
                blobResource.Meta = meta;
                return blobResource;
            }

            if (blob is not null)
            {
                return new BlobResourceContents
                {
                    Uri = uri ?? string.Empty,
                    MimeType = mimeType,
                    Blob = blob.Value,
                    Meta = meta,
                };
            }

            if (text is not null)
            {
                return new TextResourceContents
                {
                    Uri = uri ?? string.Empty,
                    MimeType = mimeType,
                    Text = text,
                    Meta = meta,
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
            if (value.MimeType is not null)
            {
                writer.WriteString("mimeType", value.MimeType);
            }

            Debug.Assert(value is BlobResourceContents or TextResourceContents);
            if (value is BlobResourceContents blobResource)
            {
                writer.WriteString("blob", blobResource.Blob.Span);
            }
            else if (value is TextResourceContents textResource)
            {
                writer.WriteString("text", textResource.Text);
            }

            if (value.Meta is not null)
            {
                writer.WritePropertyName("_meta");
                JsonSerializer.Serialize(writer, value.Meta, McpJsonUtilities.JsonContext.Default.JsonObject);
            }

            writer.WriteEndObject();
        }
    }
}
