using ModelContextProtocol.Client;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a reference to a resource or prompt in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// References are commonly used with <see cref="McpClient.CompleteAsync"/> to request completion suggestions for arguments,
/// and with other methods that need to reference resources or prompts.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class Reference
{
    /// <summary>Prevent external derivations.</summary>
    private protected Reference() 
    {
    }

    /// <summary>
    /// Gets or sets the type of content.
    /// </summary>
    /// <remarks>
    /// This can be "ref/resource" or "ref/prompt".
    /// </remarks>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="Reference"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<Reference>
    {
        /// <inheritdoc/>
        public override Reference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
            string? name = null;
            string? title = null;
            string? uri = null;

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

                    case "name":
                        name = reader.GetString();
                        break;

                    case "uri":
                        uri = reader.GetString();
                        break;

                    default:
                        break;
                }
            }

            // TODO: This converter exists due to the lack of downlevel support for AllowOutOfOrderMetadataProperties.

            switch (type)
            {
                case "ref/prompt":
                    if (name is null)
                    {
                        throw new JsonException("Prompt references must have a 'name' property.");
                    }

                    return new PromptReference { Name = name, Title = title };

                case "ref/resource":
                    if (uri is null)
                    {
                        throw new JsonException("Resource references must have a 'uri' property.");
                    }

                    return new ResourceTemplateReference { Uri = uri };

                default:
                    throw new JsonException($"Unknown content type: '{type}'");
            }
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, Reference value, JsonSerializerOptions options)
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
                case PromptReference pr:
                    writer.WriteString("name", pr.Name);
                    if (pr.Title is not null)
                    {
                        writer.WriteString("title", pr.Title);
                    }
                    break;

                case ResourceTemplateReference rtr:
                    writer.WriteString("uri", rtr.Uri);
                    break;
            }

            writer.WriteEndObject();
        }
    }
}

/// <summary>
/// Represents a reference to a prompt, identified by its name.
/// </summary>
public sealed class PromptReference : Reference, IBaseMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PromptReference"/> class.
    /// </summary>
    public PromptReference() => Type = "ref/prompt";

    /// <inheritdoc />
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <inheritdoc />
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"\"{Type}\": \"{Name}\"";
}

/// <summary>
/// Represents a reference to a resource or resource template definition.
/// </summary>
public sealed class ResourceTemplateReference : Reference
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceTemplateReference"/> class.
    /// </summary>
    public ResourceTemplateReference() => Type = "ref/resource";

    /// <summary>
    /// Gets or sets the URI or URI template of the resource.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string? Uri { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"\"{Type}\": \"{Uri}\"";
}