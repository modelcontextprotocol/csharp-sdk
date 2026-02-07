using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the binary contents of a resource in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlobResourceContents"/> is used when binary data needs to be exchanged through
/// the Model Context Protocol. The binary data is represented as base64-encoded UTF-8 bytes
/// in the <see cref="Blob"/> property.
/// </para>
/// <para>
/// This class inherits from <see cref="ResourceContents"/>, which also has a sibling implementation
/// <see cref="TextResourceContents"/> for text-based resources. When working with resources, the
/// appropriate type is chosen based on the nature of the content.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for more details.
/// </para>
/// </remarks>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class BlobResourceContents : ResourceContents
{
    private ReadOnlyMemory<byte>? _decodedData;
    private ReadOnlyMemory<byte> _blob;

    /// <summary>
    /// Creates an <see cref="BlobResourceContents"/> from raw data.
    /// </summary>
    /// <param name="data">The raw data.</param>
    /// <param name="uri">The URI of the data.</param>
    /// <param name="mimeType">The optional MIME type of the data.</param>
    /// <returns>A new <see cref="BlobResourceContents"/> instance.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static BlobResourceContents FromData(ReadOnlyMemory<byte> data, string uri, string? mimeType = null)
    {
        ReadOnlyMemory<byte> blob = Base64Helpers.EncodeToBase64Utf8(data);
        
        return new()
        {
            _decodedData = data,
            Blob = blob,
            MimeType = mimeType,
            Uri = uri
        };
    }

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the binary data of the item.
    /// </summary>
    /// <remarks>
    /// Setting this value will invalidate any cached value of <see cref="DecodedData"/>.
    /// </remarks>
    [JsonPropertyName("blob")]
    public required ReadOnlyMemory<byte> Blob
    {
        get => _blob;
        set
        {
            _blob = value;
            _decodedData = null; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets or sets the decoded data represented by <see cref="Blob"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When getting, this member will decode the value in <see cref="Blob"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Blob"/> is modified.
    /// </para>
    /// <para>
    /// When setting, the binary data is stored and <see cref="Blob"/> is updated
    /// with the base64-encoded UTF-8 representation.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData is null)
            {
                _decodedData = Base64Helpers.DecodeFromBase64Utf8(Blob);
            }
            return _decodedData.Value;
        }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string lengthDisplay = _decodedData is null ? DebuggerDisplayHelper.GetBase64LengthDisplay(Blob) : $"{DecodedData.Length} bytes";
            string mimeInfo = MimeType is not null ? $", MimeType = {MimeType}" : "";
            return $"Uri = \"{Uri}\"{mimeInfo}, Length = {lengthDisplay}";
        }
    }
}
