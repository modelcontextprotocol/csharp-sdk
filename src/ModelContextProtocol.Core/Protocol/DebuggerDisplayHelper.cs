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
        if (System.Buffers.Text.Base64.IsValid(System.Text.Encoding.UTF8.GetBytes(base64Data), out int decodedLength))
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
}
