using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Text;

namespace ModelContextProtocol;

/// <summary>Provides helper methods for encoding operations.</summary>
internal static class EncodingUtilities
{
    /// <summary>
    /// Converts UTF-16 characters to UTF-8 bytes without intermediate string allocations.
    /// </summary>
    /// <param name="utf16">The UTF-16 character span to convert.</param>
    /// <returns>A byte array containing the UTF-8 encoded bytes.</returns>
    public static byte[] GetUtf8Bytes(ReadOnlySpan<char> utf16)
    {
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(utf16)];
        Encoding.UTF8.GetBytes(utf16, bytes);
        return bytes;
    }

    /// <summary>
    /// Encodes binary data to base64-encoded UTF-8 bytes.
    /// </summary>
    /// <param name="data">The binary data to encode.</param>
    /// <returns>A ReadOnlyMemory containing the base64-encoded UTF-8 bytes.</returns>
    public static ReadOnlyMemory<byte> EncodeToBase64Utf8(ReadOnlyMemory<byte> data)
    {
        int maxLength = Base64.GetMaxEncodedToUtf8Length(data.Length);
        byte[] buffer = new byte[maxLength];
        OperationStatus status = Base64.EncodeToUtf8(data.Span, buffer, out _, out int bytesWritten);
        Debug.Assert(status == OperationStatus.Done, "Base64 encoding should succeed for valid input data");
        Debug.Assert(bytesWritten == buffer.Length, "Base64 encoding should always produce the same length as the max length");
        return buffer.AsMemory(0, bytesWritten);
    }

    /// <summary>
    /// Decodes base64-encoded UTF-8 bytes to binary data.
    /// </summary>
    /// <param name="base64Data">The base64-encoded UTF-8 bytes to decode.</param>
    /// <returns>A ReadOnlyMemory containing the decoded binary data.</returns>
    /// <exception cref="FormatException">The input is not valid base64 data.</exception>
    public static ReadOnlyMemory<byte> DecodeFromBase64Utf8(ReadOnlyMemory<byte> base64Data)
    {
        int maxLength = Base64.GetMaxDecodedFromUtf8Length(base64Data.Length);
        byte[] buffer = new byte[maxLength];
        if (Base64.DecodeFromUtf8(base64Data.Span, buffer, out _, out int bytesWritten) == OperationStatus.Done)
        {
            // Base64 decoding may produce fewer bytes than the max length, due to whitespace anywhere in the string or padding.
            Debug.Assert(bytesWritten <= buffer.Length, "Base64 decoding should never produce more bytes than the max length");
            return buffer.AsMemory(0, bytesWritten);
        }
        else
        {
            throw new FormatException("Invalid base64 data");
        }
    }
}
