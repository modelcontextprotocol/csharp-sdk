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
        return "invalid base64";
#else
        try
        {
            byte[] decoded = Convert.FromBase64String(base64Data);
            return $"{decoded.Length} bytes";
        }
        catch
        {
            return "invalid base64";
        }
#endif
    }
}
