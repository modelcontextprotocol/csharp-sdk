using System;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Internal helper methods for DebuggerDisplay implementations.
/// </summary>
internal static class DebuggerDisplayHelper
{
    /// <summary>
    /// Gets the decoded length of base64 data for debugger display.
    /// </summary>
    internal static string GetBase64LengthDisplay(string base64Data)
    {
#if NET
        if (System.Buffers.Text.Base64.IsValid(base64Data, out int decodedLength))
        {
            return $"{decodedLength} bytes";
        }
#else
        try
        {
            return $"{Convert.FromBase64String(base64Data).Length} bytes";
        }
        catch { }
#endif

        return "invalid base64";
    }

    /// <summary>
    /// Gets the decoded length of base64 data (encoded as UTF-8 bytes) for debugger display.
    /// </summary>
    internal static string GetBase64LengthDisplay(ReadOnlySpan<byte> base64Utf8Data)
    {
#if NET
        if (System.Buffers.Text.Base64.IsValid(base64Utf8Data, out int decodedLength))
        {
            return $"{decodedLength} bytes";
        }
#else
        int len = base64Utf8Data.Length;
        if (len != 0 && (len & 3) == 0)
        {
            int padding = 0;
            if (base64Utf8Data[^1] == (byte)'=') padding++;
            if (len > 1 && base64Utf8Data[^2] == (byte)'=') padding++;

            int decodedLength = (len / 4) * 3 - padding;
            return $"{decodedLength} bytes";
        }
#endif

        return "invalid base64";
    }
}
