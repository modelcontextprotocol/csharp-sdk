using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// based on the <see cref="Type"/> property. Derived types like <see cref="TextContentBlock"/>, <see cref="Utf8TextContentBlock"/>,
/// <see cref="ImageContentBlock"/>, and <see cref="EmbeddedResourceBlock"/> provide the type-specific content.
/// </para>
/// <para>
/// This class is used throughout the MCP for representing content in messages, tool responses,
/// and other communication between clients and servers.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public abstract class ContentBlock
{
    /// <summary>Prevent external derivations.</summary>
    private protected ContentBlock()
    {
    }

    /// <summary>
    /// When overridden in a derived class, gets the type of content.
    /// </summary>
    /// <value>
    /// The type of content. Valid values include "image", "audio", "text", "resource", "resource_link", "tool_use", and "tool_result".
    /// </value>
    /// <remarks>
    /// This value determines the structure of the content object.
    /// </remarks>
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    /// <summary>
    /// Gets or sets optional annotations for the content.
    /// </summary>
    /// <remarks>
    /// These annotations can be used to specify the intended audience (<see cref="Role.User"/>, <see cref="Role.Assistant"/>, or both)
    /// and the priority level of the content. Clients can use this information to filter or prioritize content for different roles.
    /// </remarks>
    [JsonPropertyName("annotations")]
    public Annotations? Annotations { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// Implementations must not make assumptions about its contents.
    /// </remarks>
    [JsonPropertyName("_meta")]
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="ContentBlock"/>.
    /// </summary>
    /// Provides a polymorphic converter for the <see cref="ContentBlock"/> class that doesn't  require
    /// setting <see cref="JsonSerializerOptions.AllowOutOfOrderMetadataProperties"/> explicitly.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class Converter : JsonConverter<ContentBlock>
    {
        private readonly bool _materializeUtf8TextContentBlocks;

        /// <summary>Initializes a new instance of the <see cref="Converter"/> class.</summary>
        public Converter()
        {
        }

        internal Converter(bool materializeUtf8TextContentBlocks) =>
            _materializeUtf8TextContentBlocks = materializeUtf8TextContentBlocks;

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
            ReadOnlyMemory<byte>? utf8Text = null;
            string? name = null;
            ReadOnlyMemory<byte>? dataUtf8 = null;
            string? mimeType = null;
            string? uri = null;
            string? description = null;
            long? size = null;
            ResourceContents? resource = null;
            Annotations? annotations = null;
            JsonObject? meta = null;
            string? id = null;
            JsonElement? input = null;
            string? toolUseId = null;
            List<ContentBlock>? content = null;
            JsonElement? structuredContent = null;
            bool? isError = null;

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
                        // Always read the JSON string token into UTF-8 bytes directly (including unescaping) without
                        // allocating an intermediate UTF-16 string. The choice of materialized type happens later.
                        utf8Text = ReadUtf8StringValueAsBytes(ref reader);
                        break;

                    case "name":
                        name = reader.GetString();
                        break;

                    case "data":
                        dataUtf8 = ReadUtf8StringValueAsBytes(ref reader);
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

                    case "id":
                        id = reader.GetString();
                        break;

                    case "input":
                        input = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.JsonElement);
                        break;

                    case "toolUseId":
                        toolUseId = reader.GetString();
                        break;

                    case "content":
                        if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            content = [];
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                content.Add(Read(ref reader, typeof(ContentBlock), options) ??
                                    throw new JsonException("Unexpected null item in content array."));
                            }
                        }
                        else
                        {
                            content = [Read(ref reader, typeof(ContentBlock), options) ??
                                throw new JsonException("Unexpected null content item.")];
                        }
                        break;

                    case "structuredContent":
                        structuredContent = JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.JsonElement);
                        break;

                    case "isError":
                        isError = reader.GetBoolean();
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }

            ContentBlock block = type switch
            {
                "text" => _materializeUtf8TextContentBlocks
                    ? new Utf8TextContentBlock
                    {
                        Utf8Text = utf8Text ?? throw new JsonException("Text contents must be provided for 'text' type."),
                    }
                    : new TextContentBlock
                    {
                        Utf8Text = utf8Text ?? throw new JsonException("Text contents must be provided for 'text' type."),
                    },

                "image" => new ImageContentBlock
                {
                    DataUtf8 = dataUtf8 ?? throw new JsonException("Image data must be provided for 'image' type."),
                    MimeType = mimeType ?? throw new JsonException("MIME type must be provided for 'image' type."),
                },

                "audio" => new AudioContentBlock
                {
                    DataUtf8 = dataUtf8 ?? throw new JsonException("Audio data must be provided for 'audio' type."),
                    MimeType = mimeType ?? throw new JsonException("MIME type must be provided for 'audio' type."),
                },

                "resource" => new EmbeddedResourceBlock
                {
                    Resource = resource ?? throw new JsonException("Resource contents must be provided for 'resource' type."),
                },

                "resource_link" => new ResourceLinkBlock
                {
                    Uri = uri ?? throw new JsonException("URI must be provided for 'resource_link' type."),
                    Name = name ?? throw new JsonException("Name must be provided for 'resource_link' type."),
                    Description = description,
                    MimeType = mimeType,
                    Size = size,
                },

                "tool_use" => new ToolUseContentBlock
                {
                    Id = id ?? throw new JsonException("ID must be provided for 'tool_use' type."),
                    Name = name ?? throw new JsonException("Name must be provided for 'tool_use' type."),
                    Input = input ?? throw new JsonException("Input must be provided for 'tool_use' type."),
                },

                "tool_result" => new ToolResultContentBlock
                {
                    ToolUseId = toolUseId ?? throw new JsonException("ToolUseId must be provided for 'tool_result' type."),
                    Content = content ?? throw new JsonException("Content must be provided for 'tool_result' type."),
                    StructuredContent = structuredContent,
                    IsError = isError,
                },

                _ => throw new JsonException($"Unknown content type: '{type}'"),
            };

            block.Annotations = annotations;
            block.Meta = meta;

            return block;
        }

        internal static ReadOnlyMemory<byte> ReadUtf8StringValueAsBytes(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException();
            }

            // If the JSON string contained no escape sequences, STJ exposes the UTF-8 bytes directly.
            if (!reader.ValueIsEscaped)
            {
                return reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
            }

            // The value is escaped (e.g. contains \uXXXX or \n); unescape into UTF-8 bytes.
            ReadOnlySpan<byte> escaped = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
            return Core.McpTextUtilities.UnescapeJsonStringToUtf8(escaped);
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
                case Utf8TextContentBlock utf8TextContent:
                    writer.WriteString("text", utf8TextContent.Utf8Text.Span);
                    break;

                case TextContentBlock textContent:
                    // Prefer UTF-8 bytes to avoid materializing a UTF-16 string for serialization.
                    if (!textContent.Utf8Text.IsEmpty)
                    {
                        writer.WriteString("text", textContent.Utf8Text.Span);
                    }
                    else
                    {
                        writer.WriteString("text", textContent.Text);
                    }
                    break;

                case ImageContentBlock imageContent:
                    if (imageContent.HasDataUtf8)
                    {
                        writer.WriteString("data", imageContent.GetDataUtf8Span());
                    }
                    else
                    {
                        writer.WriteString("data", imageContent.Data);
                    }
                    writer.WriteString("mimeType", imageContent.MimeType);
                    break;

                case AudioContentBlock audioContent:
                    if (audioContent.HasDataUtf8)
                    {
                        writer.WriteString("data", audioContent.GetDataUtf8Span());
                    }
                    else
                    {
                        writer.WriteString("data", audioContent.Data);
                    }
                    writer.WriteString("mimeType", audioContent.MimeType);
                    break;

                case EmbeddedResourceBlock embeddedResource:
                    writer.WritePropertyName("resource");
                    JsonSerializer.Serialize(writer, embeddedResource.Resource, McpJsonUtilities.JsonContext.Default.ResourceContents);
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

                case ToolUseContentBlock toolUse:
                    writer.WriteString("id", toolUse.Id);
                    writer.WriteString("name", toolUse.Name);
                    writer.WritePropertyName("input");
                    JsonSerializer.Serialize(writer, toolUse.Input, McpJsonUtilities.JsonContext.Default.JsonElement);
                    break;

                case ToolResultContentBlock toolResult:
                    writer.WriteString("toolUseId", toolResult.ToolUseId);
                    writer.WritePropertyName("content");
                    writer.WriteStartArray();
                    foreach (var item in toolResult.Content)
                    {
                        Write(writer, item, options);
                    }
                    writer.WriteEndArray();
                    if (toolResult.StructuredContent.HasValue)
                    {
                        writer.WritePropertyName("structuredContent");
                        JsonSerializer.Serialize(writer, toolResult.StructuredContent.Value, McpJsonUtilities.JsonContext.Default.JsonElement);
                    }
                    if (toolResult.IsError.HasValue)
                    {
                        writer.WriteBoolean("isError", toolResult.IsError.Value);
                    }
                    break;
            }

            if (value.Annotations is { } annotations)
            {
                writer.WritePropertyName("annotations");
                JsonSerializer.Serialize(writer, annotations, McpJsonUtilities.JsonContext.Default.Annotations);
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

/// <summary>Represents text provided to or from an LLM.</summary>
[DebuggerDisplay("Text = \"{Text}\"")]
public sealed class TextContentBlock : ContentBlock
{
    private string? _text;
    private ReadOnlyMemory<byte> _utf8Text;

    /// <inheritdoc/>
    public override string Type => "text";

    /// <summary>
    /// Gets or sets the UTF-8 encoded text content.
    /// </summary>
    /// <remarks>
    /// This enables avoiding intermediate UTF-16 string materialization when deserializing JSON.
    /// Setting this value will invalidate any cached value of <see cref="Text"/>.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> Utf8Text
    {
        get => _utf8Text;
        set
        {
            _utf8Text = value;
            _text = null; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    /// <remarks>
    /// The getter lazily materializes and caches a UTF-16 string from <see cref="Utf8Text"/>.
    /// The setter updates <see cref="Utf8Text"/>.
    /// </remarks>
    [JsonPropertyName("text")]
    public string Text
    {
        get => _text ??= Core.McpTextUtilities.GetStringFromUtf8(_utf8Text.Span);
        set
        {
            _text = value;
            _utf8Text = string.IsNullOrEmpty(value) ? null : System.Text.Encoding.UTF8.GetBytes(value);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Text ?? "";
}

/// <summary>
/// Represents text provided to or from an LLM in pre-encoded UTF-8 form.
/// </summary>
/// <remarks>
/// This type exists to avoid materializing UTF-16 strings in hot paths when the text content is already
/// available as UTF-8 bytes (for example, JSON serialized tool results).
/// </remarks>
[DebuggerDisplay("Utf8TextLength = {Utf8Text.Length}")]
public sealed class Utf8TextContentBlock : ContentBlock
{
    /// <inheritdoc/>
    public override string Type => "text";

    /// <summary>Gets or sets the UTF-8 encoded text content.</summary>
    [JsonIgnore]
    public required ReadOnlyMemory<byte> Utf8Text { get; set; }

    /// <summary>Gets the UTF-16 string representation of <see cref="Utf8Text"/>.</summary>
    [JsonIgnore]
    public string Text
    {
        get
        {
            return Core.McpTextUtilities.GetStringFromUtf8(Utf8Text.Span);
        }
    }

    /// <summary>Converts a <see cref="Utf8TextContentBlock"/> to a <see cref="TextContentBlock"/>.</summary>
    public static implicit operator TextContentBlock(Utf8TextContentBlock utf8)
    {
        Throw.IfNull(utf8);

        return new TextContentBlock
        {
            Text = utf8.Text,
            Annotations = utf8.Annotations,
            Meta = utf8.Meta,
        };
    }

    /// <summary>Converts a <see cref="TextContentBlock"/> to a <see cref="Utf8TextContentBlock"/>.</summary>
    public static implicit operator Utf8TextContentBlock(TextContentBlock text)
    {
        Throw.IfNull(text);

        return new Utf8TextContentBlock
        {
            Utf8Text = System.Text.Encoding.UTF8.GetBytes(text.Text),
            Annotations = text.Annotations,
            Meta = text.Meta,
        };
    }

    /// <inheritdoc/>
    public override string ToString() => Text;
}

/// <summary>Represents an image provided to or from an LLM.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class ImageContentBlock : ContentBlock
{
    private ReadOnlyMemory<byte> _dataUtf8;
    private ReadOnlyMemory<byte> _decodedData;
    private string? _data;

    /// <inheritdoc/>
    public override string Type => "image";

    /// <summary>
    /// Gets or sets the base64-encoded image data.
    /// </summary>
    [JsonPropertyName("data")]
    public string Data
    {
        get => _data ??= !_dataUtf8.IsEmpty
            ? Core.McpTextUtilities.GetStringFromUtf8(_dataUtf8.Span)
            : string.Empty;
        set
        {
            _data = value;
            _dataUtf8 = System.Text.Encoding.UTF8.GetBytes(value);
            _decodedData = default; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the value of <see cref="Data"/>.
    /// </summary>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DataUtf8
    {
        get => _dataUtf8.IsEmpty
            ? _data is null
                ? ReadOnlyMemory<byte>.Empty
                : System.Text.Encoding.UTF8.GetBytes(_data)
            : _dataUtf8;
        set
        {
            _data = null;
            _dataUtf8 = value;
            _decodedData = default; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets the decoded image data represented by <see cref="DataUtf8"/>.
    /// </summary>
    /// <remarks>
    /// Accessing this member will decode the value in <see cref="DataUtf8"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Data"/> or <see cref="DataUtf8"/> is modified.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData.IsEmpty)
            {
                if (_data is not null)
                {
                    _decodedData = Convert.FromBase64String(_data);
                    return _decodedData;
                }

                int maxLength = Base64.GetMaxDecodedFromUtf8Length(DataUtf8.Length);
                byte[] buffer = new byte[maxLength];
                if (Base64.DecodeFromUtf8(DataUtf8.Span, buffer, out _, out int bytesWritten) == OperationStatus.Done)
                {
                    _decodedData = bytesWritten == maxLength ? buffer : buffer.AsMemory(0, bytesWritten).ToArray();
                }
                else
                {
                    throw new FormatException("Invalid base64 data");
                }
            }

            return _decodedData;
        }
    }

    /// <summary>
    /// Gets or sets the MIME type (or "media type") of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// Common values include "image/png" and "image/jpeg".
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }

    internal bool HasDataUtf8 => !_dataUtf8.IsEmpty;

    internal ReadOnlySpan<byte> GetDataUtf8Span() => _dataUtf8.Span;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"MimeType = {MimeType}, Length = {DebuggerDisplayHelper.GetBase64LengthDisplay(Data)}";
}

/// <summary>Represents audio provided to or from an LLM.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class AudioContentBlock : ContentBlock
{
    private ReadOnlyMemory<byte> _dataUtf8;
    private ReadOnlyMemory<byte> _decodedData;
    private string? _data;

    /// <inheritdoc/>
    public override string Type => "audio";

    /// <summary>
    /// Gets or sets the base64-encoded audio data.
    /// </summary>
    [JsonPropertyName("data")]
    public string Data
    {
        get => _data ??= !_dataUtf8.IsEmpty
            ? Core.McpTextUtilities.GetStringFromUtf8(_dataUtf8.Span)
            : string.Empty;
        set
        {
            _data = value;
            _dataUtf8 = System.Text.Encoding.UTF8.GetBytes(value);
            _decodedData = default; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the value of <see cref="Data"/>.
    /// </summary>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DataUtf8
    {
        get => _dataUtf8.IsEmpty
            ? _data is null
                ? ReadOnlyMemory<byte>.Empty
                : System.Text.Encoding.UTF8.GetBytes(_data)
            : _dataUtf8;
        set
        {
            _data = null;
            _dataUtf8 = value;
            _decodedData = default; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets the decoded audio data represented by <see cref="DataUtf8"/>.
    /// </summary>
    /// <remarks>
    /// Accessing this member will decode the value in <see cref="DataUtf8"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Data"/> or <see cref="DataUtf8"/> is modified.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData.IsEmpty)
            {
                if (_data is not null)
                {
                    _decodedData = Convert.FromBase64String(_data);
                    return _decodedData;
                }

                int maxLength = Base64.GetMaxDecodedFromUtf8Length(DataUtf8.Length);
                byte[] buffer = new byte[maxLength];
                if (Base64.DecodeFromUtf8(DataUtf8.Span, buffer, out _, out int bytesWritten) == OperationStatus.Done)
                {
                    _decodedData = bytesWritten == maxLength ? buffer : buffer.AsMemory(0, bytesWritten).ToArray();
                }
                else
                {
                    throw new FormatException("Invalid base64 data");
                }
            }

            return _decodedData;
        }
    }

    /// <summary>
    /// Gets or sets the MIME type (or "media type") of the content, specifying the format of the data.
    /// </summary>
    /// <remarks>
    /// Common values include "audio/wav" and "audio/mp3".
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }

    internal bool HasDataUtf8 => !_dataUtf8.IsEmpty;

    internal ReadOnlySpan<byte> GetDataUtf8Span() => _dataUtf8.Span;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"MimeType = {MimeType}, Length = {DebuggerDisplayHelper.GetBase64LengthDisplay(Data)}";
}

/// <summary>Represents the contents of a resource, embedded into a prompt or tool call result.</summary>
/// <remarks>
/// It is up to the client how best to render embedded resources for the benefit of the LLM and/or the user.
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class EmbeddedResourceBlock : ContentBlock
{
    /// <inheritdoc/>
    public override string Type => "resource";

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

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => $"Uri = \"{Resource.Uri}\"";
}

/// <summary>Represents a resource that the server is capable of reading, included in a prompt or tool call result.</summary>
/// <remarks>
/// Resource links returned by tools are not guaranteed to appear in the results of `resources/list` requests.
/// </remarks>
[DebuggerDisplay("Name = {Name}, Uri = \"{Uri}\"")]
public sealed class ResourceLinkBlock : ContentBlock
{
    /// <inheritdoc/>
    public override string Type => "resource_link";

    /// <summary>
    /// Gets or sets the URI of this resource.
    /// </summary>
    [JsonPropertyName("uri")]
    [StringSyntax(StringSyntaxAttribute.Uri)]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets a human-readable name for this resource.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets a description of what this resource represents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This description can be used by clients to improve the LLM's understanding of available resources. It can be thought of like a \"hint\" to the model.
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
    public string? Description { get; set; }

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
    /// This property can be <see langword="null"/> if the MIME type is unknown or not applicable for the resource.
    /// </para>
    /// </remarks>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    /// <summary>
    /// Gets or sets the size of the raw resource content (before base64 encoding), in bytes, if known.
    /// </summary>
    /// <remarks>
    /// This value can be used by applications to display file sizes and estimate context window usage.
    /// </remarks>
    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

/// <summary>Represents a request from the assistant to call a tool.</summary>
[DebuggerDisplay("Name = {Name}, Id = {Id}")]
public sealed class ToolUseContentBlock : ContentBlock
{
    /// <inheritdoc/>
    public override string Type => "tool_use";

    /// <summary>
    /// Gets or sets a unique identifier for this tool use.
    /// </summary>
    /// <remarks>
    /// This ID is used to match tool results to their corresponding tool uses.
    /// </remarks>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the tool to call.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the arguments to pass to the tool, conforming to the tool's input schema.
    /// </summary>
    [JsonPropertyName("input")]
    public required JsonElement Input { get; set; }
}

/// <summary>Represents the result of a tool use, provided by the user back to the assistant.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class ToolResultContentBlock : ContentBlock
{
    /// <inheritdoc/>
    public override string Type => "tool_result";

    /// <summary>
    /// Gets or sets the ID of the tool use this result corresponds to.
    /// </summary>
    /// <remarks>
    /// This value must match the ID from a previous <see cref="ToolUseContentBlock"/>.
    /// </remarks>
    [JsonPropertyName("toolUseId")]
    public required string ToolUseId { get; set; }

    /// <summary>
    /// Gets or sets the unstructured result content of the tool use.
    /// </summary>
    /// <remarks>
    /// This value has the same format as CallToolResult.Content and can include text, images,
    /// audio, resource links, and embedded resources.
    /// </remarks>
    [JsonPropertyName("content")]
    public required List<ContentBlock> Content { get; set; }

    /// <summary>
    /// Gets or sets an optional structured result object.
    /// </summary>
    /// <remarks>
    /// If the tool defined an outputSchema, this object should conform to that schema.
    /// </remarks>
    [JsonPropertyName("structuredContent")]
    public JsonElement? StructuredContent { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool use resulted in an error.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool use resulted in an error; <see langword="false"/> if it succeeded. The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// If <see langword="true"/>, the content typically describes the error that occurred.
    /// </remarks>
    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            if (IsError == true)
            {
                return $"ToolUseId = {ToolUseId}, IsError = true";
            }

            // Try to show the result content
            if (Content.Count == 1 && Content[0] is TextContentBlock textBlock)
            {
                return $"ToolUseId = {ToolUseId}, Result = \"{textBlock.Text}\"";
            }

            if (StructuredContent.HasValue)
            {
                try
                {
                    string json = StructuredContent.Value.GetRawText();
                    return $"ToolUseId = {ToolUseId}, Result = {json}";
                }
                catch
                {
                    // Fall back to content count if GetRawText fails
                }
            }

            return $"ToolUseId = {ToolUseId}, ContentCount = {Content.Count}";
        }
    }
}
