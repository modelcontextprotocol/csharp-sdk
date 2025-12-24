using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace ModelContextProtocol.Core;

/// <summary>
/// Provides helpers for working with UTF-8 data across all target frameworks.
/// </summary>
public static class McpTextUtilities
{
    /// <summary>
    /// Decodes the provided UTF-8 bytes into a <see cref="string"/>.
    /// Uses a pointer-based overload on TFM <c>netstandard2.0</c>.
    /// (The specific method differs by target framework.)
    /// </summary>
    public static string GetStringFromUtf8(ReadOnlySpan<byte> utf8Bytes)
    {
#if NET
        return Encoding.UTF8.GetString(utf8Bytes);
#else
        if (utf8Bytes.IsEmpty)
        {
            return string.Empty;
        }

        unsafe
        {
            fixed (byte* p = utf8Bytes)
            {
                return Encoding.UTF8.GetString(p, utf8Bytes.Length);
            }
        }
#endif
    }

    /// <summary>
    /// Encodes the provided binary data into a base64 string.
    /// Uses a span-based overload on TFM <c>net</c>.
    /// </summary>
    public static string GetBase64String(ReadOnlyMemory<byte> data)
    {
#if NET
        return Convert.ToBase64String(data.Span);
#else
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment))
        {
            return Convert.ToBase64String(segment.Array!, segment.Offset, segment.Count);
        }

        return Convert.ToBase64String(data.ToArray());
#endif
    }

    /// <summary>
    /// Determines whether the provided UTF-8 bytes consist only of whitespace characters
    /// commonly found in MCP transports (space, tab, carriage return).
    /// </summary>
    public static bool IsWhiteSpace(ReadOnlySpan<byte> utf8Bytes)
    {
        for (int i = 0; i < utf8Bytes.Length; i++)
        {
            byte b = utf8Bytes[i];
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            {
                return false;
            }
        }

        return true;
    }
}