using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents the content of a resource.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// </summary>
[JsonConverter(typeof(ResourceContentsConverter))]
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
    /// The type of content.
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

internal class ResourceContentsConverter : JsonConverter<ResourceContents>
{
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

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            string? propertyName = reader.GetString();

            if (propertyName == null)
            {
                continue;
            }

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

    public override void Write(Utf8JsonWriter writer, ResourceContents value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("uri", value.Uri);
        writer.WriteString("mimeType", value.MimeType);
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