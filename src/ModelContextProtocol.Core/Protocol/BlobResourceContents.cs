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
public sealed class BlobResourceContents : ResourceContents
{
    private byte[]? _decodedData;

    /// <summary>
    /// Gets or sets the base64-encoded UTF-8 bytes representing the binary data of the item.
    /// </summary>
    /// <remarks>
    /// This is a zero-copy representation of the wire payload of this item. Setting this value will invalidate any cached value of <see cref="Data"/>.
    /// </remarks>
    [JsonPropertyName("blob")]
    public required ReadOnlyMemory<byte> Blob { get; set; }

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
#if NET6_0_OR_GREATER
                _decodedData = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(Blob.Span));
#else
                _decodedData = Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(Blob.ToArray()));
#endif
            }
            return _decodedData;
        }
    }
}
