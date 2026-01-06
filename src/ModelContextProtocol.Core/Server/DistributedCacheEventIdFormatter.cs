// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// This is a shared source file included in both ModelContextProtocol.Core and the test project.
// Do not reference symbols internal to the core project, as they won't be available in tests.

using System;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides methods for formatting and parsing event IDs used by <see cref="DistributedCacheEventStreamStore"/>.
/// </summary>
/// <remarks>
/// Event IDs are formatted as "{base64(sessionId)}:{base64(streamId)}:{sequence}".
/// Base64 encoding is used because the MCP specification allows session IDs to contain
/// any visible ASCII character (0x21-0x7E), including the ':' separator character.
/// </remarks>
internal static class DistributedCacheEventIdFormatter
{
    private const char Separator = ':';

    /// <summary>
    /// Formats session ID, stream ID, and sequence number into an event ID string.
    /// </summary>
    public static string Format(string sessionId, string streamId, long sequence)
    {
        // Base64-encode session and stream IDs so the event ID can be parsed
        // even if the original IDs contain the ':' separator character
        var sessionBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sessionId));
        var streamBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(streamId));
        return $"{sessionBase64}{Separator}{streamBase64}{Separator}{sequence}";
    }

    /// <summary>
    /// Attempts to parse an event ID into its component parts.
    /// </summary>
    public static bool TryParse(string eventId, out string sessionId, out string streamId, out long sequence)
    {
        sessionId = string.Empty;
        streamId = string.Empty;
        sequence = 0;

        var parts = eventId.Split(Separator);
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            sessionId = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            streamId = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            return long.TryParse(parts[2], out sequence);
        }
        catch
        {
            return false;
        }
    }
}
