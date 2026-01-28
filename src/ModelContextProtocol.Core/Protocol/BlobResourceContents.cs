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
/// in the <see cref="Blob"/> property, providing a zero-copy representation of the wire payload.
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
    private byte[]? _decodedData;
    private ReadOnlyMemory<byte> _blob;

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the binary data of the item.
    /// </summary>
    /// <remarks>
    /// This is a zero-copy representation of the wire payload of this item. Setting this value will invalidate any cached value of <see cref="Data"/>.
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
    /// Gets the decoded data represented by <see cref="Blob"/>.
    /// </summary>
    /// <remarks>
    /// Accessing this member will decode the value in <see cref="Blob"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="Blob"/> is modified.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> Data
    {
        get
        {
            if (_decodedData is null)
            {
#if NET
                // Decode directly from UTF-8 base64 bytes without string intermediate
                int maxLength = Base64.GetMaxDecodedFromUtf8Length(Blob.Length);
                byte[] buffer = new byte[maxLength];
                if (Base64.DecodeFromUtf8(Blob.Span, buffer, out _, out int bytesWritten) == System.Buffers.OperationStatus.Done)
                {
                    _decodedData = bytesWritten == maxLength ? buffer : buffer.AsMemory(0, bytesWritten).ToArray();
                }
                else
                {
                    throw new FormatException("Invalid base64 data");
                }
#else
                byte[] array = MemoryMarshal.TryGetArray(Blob, out ArraySegment<byte> segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length ? segment.Array : Blob.ToArray();
                _decodedData = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(array));
#endif
            }
            return _decodedData;
        }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay
    {
        get
        {
            string lengthDisplay = _decodedData is null ? DebuggerDisplayHelper.GetBase64LengthDisplay(Blob) : $"{_decodedData.Length} bytes";
            string mimeInfo = MimeType is not null ? $", MimeType = {MimeType}" : "";
            return $"Uri = \"{Uri}\"{mimeInfo}, Length = {lengthDisplay}";
        }
    }
}
