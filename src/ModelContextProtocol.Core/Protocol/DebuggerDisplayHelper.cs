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
    /// Gets the decoded length of base64 data encoded as UTF-8 bytes for debugger display.
    /// </summary>
    internal static string GetBase64LengthDisplay(ReadOnlyMemory<byte> base64Data)
    {
#if NET
        if (System.Buffers.Text.Base64.IsValid(base64Data.Span, out int decodedLength))
        {
            return $"{decodedLength} bytes";
        }
#else
        try
        {
            return $"{Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(base64Data.ToArray())).Length} bytes";
        }
        catch { }
#endif

        return "invalid base64";
    }
}
