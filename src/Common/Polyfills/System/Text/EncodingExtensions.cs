// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NET

namespace System.Text;

internal static class EncodingExtensions
{
    /// <summary>
    /// Gets the number of bytes required to encode the specified characters.
    /// </summary>
    public static int GetByteCount(this Encoding encoding, ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
        {
            return 0;
        }

        unsafe
        {
            fixed (char* charsPtr = chars)
            {
                return encoding.GetByteCount(charsPtr, chars.Length);
            }
        }
    }

    /// <summary>
    /// Encodes the specified characters into the specified byte span.
    /// </summary>
    public static int GetBytes(this Encoding encoding, ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (chars.IsEmpty)
        {
            return 0;
        }

        unsafe
        {
            fixed (char* charsPtr = chars)
            fixed (byte* bytesPtr = bytes)
            {
                return encoding.GetBytes(charsPtr, chars.Length, bytesPtr, bytes.Length);
            }
        }
    }
}

#endif
