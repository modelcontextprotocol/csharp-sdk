using System.Buffers.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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

    internal static byte[] UnescapeJsonStringToUtf8(ReadOnlySpan<byte> escaped)
    {
        // Two-pass: first compute output length, then write, to avoid intermediate buffers/copies.
        int outputLength = 0;
        for (int i = 0; i < escaped.Length; i++)
        {
            byte b = escaped[i];
            if (b != (byte)'\\')
            {
                outputLength++;
                continue;
            }

            if (++i >= escaped.Length)
            {
                throw new JsonException();
            }

            switch (escaped[i])
            {
                case (byte)'"':
                case (byte)'\\':
                case (byte)'/':
                case (byte)'b':
                case (byte)'f':
                case (byte)'n':
                case (byte)'r':
                case (byte)'t':
                    outputLength++;
                    break;

                case (byte)'u':
                    outputLength += GetUtf8ByteCountForEscapedUnicode(escaped, ref i);
                    break;

                default:
                    throw new JsonException();
            }
        }

        byte[] result = new byte[outputLength];
        int dst = 0;

        for (int i = 0; i < escaped.Length; i++)
        {
            byte b = escaped[i];
            if (b != (byte)'\\')
            {
                result[dst++] = b;
                continue;
            }

            if (++i >= escaped.Length)
            {
                throw new JsonException();
            }

            byte esc = escaped[i];
            switch (esc)
            {
                case (byte)'"': result[dst++] = (byte)'"'; break;
                case (byte)'\\': result[dst++] = (byte)'\\'; break;
                case (byte)'/': result[dst++] = (byte)'/'; break;
                case (byte)'b': result[dst++] = 0x08; break;
                case (byte)'f': result[dst++] = 0x0C; break;
                case (byte)'n': result[dst++] = 0x0A; break;
                case (byte)'r': result[dst++] = 0x0D; break;
                case (byte)'t': result[dst++] = 0x09; break;

                case (byte)'u':
                    uint scalar = ReadEscapedUnicodeScalar(escaped, ref i);
                    WriteUtf8Scalar(scalar, result, ref dst);
                    break;

                default:
                    throw new JsonException();
            }
        }

        Debug.Assert(dst == result.Length);
        return result;
    }

    internal static int GetUtf8ByteCountForEscapedUnicode(ReadOnlySpan<byte> escaped, ref int i)
    {
        uint scalar = ReadEscapedUnicodeScalar(escaped, ref i);
        return scalar <= 0x7F ? 1 :
            scalar <= 0x7FF ? 2 :
            scalar <= 0xFFFF ? 3 :
            4;
    }

    internal static uint ReadEscapedUnicodeScalar(ReadOnlySpan<byte> escaped, ref int i)
    {
        // i points at 'u'.
        if (i + 4 >= escaped.Length)
        {
            throw new JsonException();
        }

        uint codeUnit = (uint)(FromHex(escaped[i + 1]) << 12 |
                               FromHex(escaped[i + 2]) << 8 |
                               FromHex(escaped[i + 3]) << 4 |
                               FromHex(escaped[i + 4]));
        i += 4;

        // Surrogate pair: \uD800-\uDBFF followed by \uDC00-\uDFFF
        if (codeUnit is >= 0xD800 and <= 0xDBFF)
        {
            int lookahead = i + 1;
            if (lookahead + 5 < escaped.Length && escaped[lookahead] == (byte)'\\' && escaped[lookahead + 1] == (byte)'u')
            {
                uint low = (uint)(FromHex(escaped[lookahead + 2]) << 12 |
                                  FromHex(escaped[lookahead + 3]) << 8 |
                                  FromHex(escaped[lookahead + 4]) << 4 |
                                  FromHex(escaped[lookahead + 5]));

                if (low is >= 0xDC00 and <= 0xDFFF)
                {
                    i = lookahead + 5;
                    return 0x10000u + ((codeUnit - 0xD800u) << 10) + (low - 0xDC00u);
                }
            }
        }

        return codeUnit;
    }

    internal static int FromHex(byte b)
    {
        if ((uint)(b - '0') <= 9) return b - '0';
        if ((uint)((b | 0x20) - 'a') <= 5) return (b | 0x20) - 'a' + 10;
        throw new JsonException();
    }

    internal static void WriteUtf8Scalar(uint scalar, byte[] destination, ref int dst)
    {
        if (scalar <= 0x7F)
        {
            destination[dst++] = (byte)scalar;
        }
        else if (scalar <= 0x7FF)
        {
            destination[dst++] = (byte)(0xC0 | (scalar >> 6));
            destination[dst++] = (byte)(0x80 | (scalar & 0x3F));
        }
        else if (scalar <= 0xFFFF)
        {
            destination[dst++] = (byte)(0xE0 | (scalar >> 12));
            destination[dst++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
            destination[dst++] = (byte)(0x80 | (scalar & 0x3F));
        }
        else
        {
            destination[dst++] = (byte)(0xF0 | (scalar >> 18));
            destination[dst++] = (byte)(0x80 | ((scalar >> 12) & 0x3F));
            destination[dst++] = (byte)(0x80 | ((scalar >> 6) & 0x3F));
            destination[dst++] = (byte)(0x80 | (scalar & 0x3F));
        }
    }
}
