using System.Buffers;
using System.Buffers.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
    /// <remarks>
    /// Provides a polymorphic converter for the <see cref="ContentBlock"/> class that doesn't require
    /// setting <see cref="JsonSerializerOptions.AllowOutOfOrderMetadataProperties"/> explicitly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ContentBlock>
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
            string? title = null;
            ReadOnlyMemory<byte>? data = null;
            ReadOnlyMemory<byte>? decodedData = null;
            string? mimeType = null;
            string? uri = null;
            string? description = null;
            long? size = null;
            IList<Icon>? icons = null;
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
                        text = reader.GetString();
                        break;

                    case "name":
                        name = reader.GetString();
                        break;

                    case "title":
                        title = reader.GetString();
                        break;

                    case "data":
                        if (!reader.ValueIsEscaped)
                        {
                            data = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                        }
                        else
                        {
                            decodedData = reader.GetBytesFromBase64();
                        }
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

                    case "icons":
                        if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            icons = [];
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                icons.Add(JsonSerializer.Deserialize(ref reader, McpJsonUtilities.JsonContext.Default.Icon) ??
                                    throw new JsonException("Unexpected null item in icons array."));
                            }
                        }
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
                "text" => new TextContentBlock
                {
                    Text = text ?? throw new JsonException("Text contents must be provided for 'text' type."),
                },

                "image" => decodedData is not null ?
                    ImageContentBlock.FromBytes(decodedData.Value,
                        mimeType ?? throw new JsonException("MIME type must be provided for 'image' type.")) :
                    new ImageContentBlock
                    {
                        Data = data ?? throw new JsonException("Image data must be provided for 'image' type."),
                        MimeType = mimeType ?? throw new JsonException("MIME type must be provided for 'image' type."),
                    },

                "audio" => decodedData is not null ?
                    AudioContentBlock.FromBytes(decodedData.Value,
                        mimeType ?? throw new JsonException("MIME type must be provided for 'audio' type.")) :
                    new AudioContentBlock
                    {
                        Data = data ?? throw new JsonException("Audio data must be provided for 'audio' type."),
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
                    Title = title,
                    Description = description,
                    MimeType = mimeType,
                    Size = size,
                    Icons = icons,
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
                    break;

                case ImageContentBlock imageContent:
                    writer.WriteString("data", imageContent.Data.Span);
                    writer.WriteString("mimeType", imageContent.MimeType);
                    break;

                case AudioContentBlock audioContent:
                    writer.WriteString("data", audioContent.Data.Span);
                    writer.WriteString("mimeType", audioContent.MimeType);
                    break;

                case EmbeddedResourceBlock embeddedResource:
                    writer.WritePropertyName("resource");
                    JsonSerializer.Serialize(writer, embeddedResource.Resource, McpJsonUtilities.JsonContext.Default.ResourceContents);
                    break;

                case ResourceLinkBlock resourceLink:
                    writer.WriteString("uri", resourceLink.Uri);
                    writer.WriteString("name", resourceLink.Name);
                    if (resourceLink.Title is not null)
                    {
                        writer.WriteString("title", resourceLink.Title);
                    }
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
                    if (resourceLink.Icons is { Count: > 0 })
                    {
                        writer.WritePropertyName("icons");
                        writer.WriteStartArray();
                        foreach (var icon in resourceLink.Icons)
                        {
                            JsonSerializer.Serialize(writer, icon, McpJsonUtilities.JsonContext.Default.Icon);
                        }
                        writer.WriteEndArray();
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
    /// <inheritdoc/>
    public override string Type => "text";

    /// <summary>
    /// Gets or sets the text content of the message.
    /// </summary>
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    /// <inheritdoc/>
    public override string ToString() => Text ?? "";
}

/// <summary>Represents an image provided to or from an LLM.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class ImageContentBlock : ContentBlock
{
    private ReadOnlyMemory<byte>? _decodedData;
    private ReadOnlyMemory<byte>? _data;

    /// <summary>
    /// Creates an <see cref="ImageContentBlock"/> from decoded image bytes.
    /// </summary>
    /// <param name="bytes">The unencoded image bytes.</param>
    /// <param name="mimeType">The MIME type of the image.</param>
    /// <returns>A new <see cref="ImageContentBlock"/> instance.</returns>
    /// <remarks>
    /// This method stores the provided bytes as <see cref="DecodedData"/> and lazily encodes them to base64 UTF-8 bytes for <see cref="Data"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mimeType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mimeType"/> is empty or composed entirely of whitespace.</exception>
    public static ImageContentBlock FromBytes(ReadOnlyMemory<byte> bytes, string mimeType)
    {
        Throw.IfNullOrWhiteSpace(mimeType);

        return new(bytes, mimeType);
    }

    /// <summary>Initializes a new instance of the <see cref="ImageContentBlock"/> class.</summary>
    public ImageContentBlock()
    {
    }

    [SetsRequiredMembers]
    private ImageContentBlock(ReadOnlyMemory<byte> decodedData, string mimeType)
    {
        _decodedData = decodedData;
        MimeType = mimeType;
    }

    /// <inheritdoc/>
    public override string Type => "image";

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the image data.
    /// </summary>
    /// <remarks>
    /// Setting this value will invalidate any cached value of <see cref="DecodedData"/>.
    /// </remarks>
    [JsonPropertyName("data")]
    public required ReadOnlyMemory<byte> Data
    {
        get
        {
            if (_data is null)
            {
                Debug.Assert(_decodedData is not null);
                _data = EncodingUtilities.EncodeToBase64Utf8(_decodedData!.Value);
            }

            return _data.Value;
        }
        set
        {
            _data = value;
            _decodedData = null; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets the decoded image data represented by <see cref="Data"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When getting, this member will decode the value in <see cref="Data"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Data"/> is modified.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData is null)
            {
                _decodedData = EncodingUtilities.DecodeFromBase64Utf8(Data);
            }

            return _decodedData.Value;
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

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string lengthDisplay = _decodedData is not null ? $"{_decodedData.Value.Length} bytes" : DebuggerDisplayHelper.GetBase64LengthDisplay(Data);
            return $"MimeType = {MimeType}, Length = {lengthDisplay}";
        }
    }
}

/// <summary>Represents audio provided to or from an LLM.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class AudioContentBlock : ContentBlock
{
    private ReadOnlyMemory<byte>? _decodedData;
    private ReadOnlyMemory<byte>? _data;

    /// <summary>
    /// Creates an <see cref="AudioContentBlock"/> from decoded audio bytes.
    /// </summary>
    /// <param name="bytes">The unencoded audio bytes.</param>
    /// <param name="mimeType">The MIME type of the audio.</param>
    /// <returns>A new <see cref="AudioContentBlock"/> instance.</returns>
    /// <remarks>
    /// This method stores the provided bytes as <see cref="DecodedData"/> and lazily encodes them to base64 UTF-8 bytes for <see cref="Data"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mimeType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="mimeType"/> is empty or composed entirely of whitespace.</exception>
    public static AudioContentBlock FromBytes(ReadOnlyMemory<byte> bytes, string mimeType)
    {
        Throw.IfNullOrWhiteSpace(mimeType);

        return new(bytes, mimeType);
    }

    /// <summary>Initializes a new instance of the <see cref="AudioContentBlock"/> class.</summary>
    public AudioContentBlock()
    {
    }

    [SetsRequiredMembers]
    private AudioContentBlock(ReadOnlyMemory<byte> decodedData, string mimeType)
    {
        _decodedData = decodedData;
        MimeType = mimeType;
    }

    /// <inheritdoc/>
    public override string Type => "audio";

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the audio data.
    /// </summary>
    /// <remarks>
    /// Setting this value will invalidate any cached value of <see cref="DecodedData"/>.
    /// </remarks>
    [JsonPropertyName("data")]
    public required ReadOnlyMemory<byte> Data
    {
        get
        {
            if (_data is null)
            {
                Debug.Assert(_decodedData is not null);
                _data = EncodingUtilities.EncodeToBase64Utf8(_decodedData!.Value);
            }

            return _data.Value;
        }
        set
        {
            _data = value;
            _decodedData = null; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets the decoded audio data represented by <see cref="Data"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When getting, this member will decode the value in <see cref="Data"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Data"/> is modified.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData is null)
            {
                _decodedData = EncodingUtilities.DecodeFromBase64Utf8(Data);
            }

            return _decodedData.Value;
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

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string lengthDisplay = _decodedData is not null ? $"{_decodedData.Value.Length} bytes" : DebuggerDisplayHelper.GetBase64LengthDisplay(Data);
            return $"MimeType = {MimeType}, Length = {lengthDisplay}";
        }
    }
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
    /// Gets or sets a title for this resource.
    /// </summary>
    /// <remarks>
    /// This is intended for UI and end-user contexts. It is optimized to be human-readable and easily understood,
    /// even by those unfamiliar with domain-specific terminology.
    /// If not provided, <see cref="Name"/> can be used for display.
    /// </remarks>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

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

    /// <summary>
    /// Gets or sets an optional list of icons for this resource.
    /// </summary>
    /// <remarks>
    /// This can be used by clients to display the resource's icon in a user interface.
    /// </remarks>
    [JsonPropertyName("icons")]
    public IList<Icon>? Icons { get; set; }
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
    public required IList<ContentBlock> Content { get; set; }

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
