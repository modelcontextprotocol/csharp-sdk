using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    private ReadOnlyMemory<byte>? _blob;

    /// <summary>
    /// Creates a <see cref="BlobResourceContents"/> from raw data.
    /// </summary>
    /// <param name="bytes">The raw unencoded data.</param>
    /// <param name="uri">The URI of the blob resource.</param>
    /// <param name="mimeType">The optional MIME type of the data.</param>
    /// <returns>A new <see cref="BlobResourceContents"/> instance.</returns>
    public static BlobResourceContents FromBytes(ReadOnlyMemory<byte> bytes, string uri, string? mimeType = null)
    {
        return new(bytes, uri, mimeType);
    }

    /// <summary>Initializes a new instance of the <see cref="BlobResourceContents"/> class.</summary>
    public BlobResourceContents()
    {
    }

    [SetsRequiredMembers]
    private BlobResourceContents(ReadOnlyMemory<byte> decodedData, string uri, string? mimeType)
    {
        _decodedData = decodedData;
        Uri = uri;
        MimeType = mimeType;
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
        get
        {
            if (_blob is null)
            {
                Debug.Assert(_decodedData is not null);
                _blob = EncodingUtilities.EncodeToBase64Utf8(_decodedData!.Value);
            }

            return _blob.Value;
        }
        set
        {
            _blob = value;
            _decodedData = null; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets the decoded data represented by <see cref="Blob"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When getting, this member will decode the value in <see cref="Blob"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Blob"/> is modified.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData is null)
            {
                _decodedData = EncodingUtilities.DecodeFromBase64Utf8(Blob);
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
