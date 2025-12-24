using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using ModelContextProtocol.Core;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the binary contents of a resource in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BlobResourceContents"/> is used when binary data needs to be exchanged through
/// the Model Context Protocol. The binary data is represented as a base64-encoded string
/// in the <see cref="Blob"/> and <see cref="BlobUtf8"/> properties and as raw bytes in
/// the <see cref="DecodedData"/> property.
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
    private ReadOnlyMemory<byte> _decodedData;
    private ReadOnlyMemory<byte> _blobUtf8;
    private string? _blob;

    /// <summary>Initializes a new instance of the <see cref="BlobResourceContents"/> class.</summary>
    [SetsRequiredMembers]
    public BlobResourceContents()
    {
        Blob = string.Empty;
        Uri = string.Empty;
    }

    /// <summary>
    /// Gets or sets the base64-encoded string representing the binary data of the item.
    /// </summary>
    [JsonPropertyName("blob")]
    public required string Blob
    {
        get
            => _blob ??= !_blobUtf8.IsEmpty
                ? McpTextUtilities.GetStringFromUtf8(_blobUtf8.Span)
                // encode _decodedData back to base64 if needed
                : McpTextUtilities.GetBase64String(_decodedData);
        set
        {
            _blob = value;
            _blobUtf8 = Encoding.UTF8.GetBytes(value);
            _decodedData = default; // Invalidate cache
        }
    }

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the binary data of the item.
    /// </summary>
    /// <remarks>
    /// This is a zero-copy representation of the wire payload of this item. Setting this value will invalidate any cached value of <see cref="DecodedData"/>.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> BlobUtf8
    {
        get => _blobUtf8.IsEmpty
            ? _blob is null
                ? _decodedData.IsEmpty
                    ? ReadOnlyMemory<byte>.Empty
                    : EncodeToUtf8(_decodedData)
                : Encoding.UTF8.GetBytes(_blob)
            : _blobUtf8;
        set
        {
            _blob = null;
            _blobUtf8 = value;
            _decodedData = default; // Invalidate cache
        }
    }

    private ReadOnlyMemory<byte> EncodeToUtf8(ReadOnlyMemory<byte> decodedData)
    {
        int maxLength = Base64.GetMaxEncodedToUtf8Length(decodedData.Length);
        byte[] buffer = new byte[maxLength];
        if (Base64.EncodeToUtf8(decodedData.Span, buffer, out _, out int bytesWritten) == System.Buffers.OperationStatus.Done)
        {
            return buffer.AsMemory(0, bytesWritten);
        }
        else
        {
            throw new FormatException("Failed to encode base64 data");
        }
    }

    [JsonIgnore]
    internal bool HasBlobUtf8 => !_blobUtf8.IsEmpty;

    internal ReadOnlySpan<byte> GetBlobUtf8Span() => _blobUtf8.Span;

    /// <summary>
    /// Gets the decoded data represented by <see cref="BlobUtf8"/>.
    /// </summary>
    /// <remarks>
    /// Accessing this member will decode the value in <see cref="BlobUtf8"/> and cache the result.
    /// Subsequent accesses return the cached value unless <see cref="BlobUtf8"/> is modified.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<byte> DecodedData
    {
        get
        {
            if (_decodedData.IsEmpty)
            {
                if (_blob is not null)
                {
                    // Decode from string representation
                    _decodedData = Convert.FromBase64String(_blob);
                    return _decodedData;
                }
                // Decode directly from UTF-8 base64 bytes without string intermediate
                int maxLength = Base64.GetMaxDecodedFromUtf8Length(BlobUtf8.Length);
                byte[] buffer = new byte[maxLength];
                if (Base64.DecodeFromUtf8(BlobUtf8.Span, buffer, out _, out int bytesWritten) == System.Buffers.OperationStatus.Done)
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
        set
        {
            _blob = null;
            _blobUtf8 = default;
            _decodedData = value;
        }
    }
}
