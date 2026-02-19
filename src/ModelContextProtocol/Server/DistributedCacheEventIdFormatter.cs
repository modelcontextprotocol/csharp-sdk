// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// This is a shared source file included in both ModelContextProtocol and the test project.
// Do not reference symbols internal to the core project, as they won't be available in tests.
#if NET
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

#endif
using System.Text;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides methods for formatting and parsing event IDs used by <see cref="DistributedCacheEventStreamStore"/>.
/// </summary>
/// <remarks>
/// Event IDs are formatted as "{base64(sessionId)}:{base64(streamId)}:{sequence}".
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
        var sessionBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sessionId));
        var streamBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(streamId));
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

#if NET
        ReadOnlySpan<char> eventIdSpan = eventId.AsSpan();
        Span<Range> partRanges = stackalloc Range[4];
        int rangeCount = eventIdSpan.Split(partRanges, Separator);
        if (rangeCount != 3)
        {
            return false;
        }

        try
        {
            ReadOnlySpan<char> sessionBase64 = eventIdSpan[partRanges[0]];
            ReadOnlySpan<char> streamBase64 = eventIdSpan[partRanges[1]];
            ReadOnlySpan<char> sequenceSpan = eventIdSpan[partRanges[2]];

            if (!TryDecodeBase64ToString(sessionBase64, out sessionId!) ||
                !TryDecodeBase64ToString(streamBase64, out streamId!))
            {
                return false;
            }

            return long.TryParse(sequenceSpan, out sequence);
        }
        catch
        {
            return false;
        }
#else
        var parts = eventId.Split(Separator);
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            sessionId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
            streamId = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            return long.TryParse(parts[2], out sequence);
        }
        catch
        {
            return false;
        }
#endif
    }

#if NET
    private static bool TryDecodeBase64ToString(ReadOnlySpan<char> base64Chars, [NotNullWhen(true)] out string? result)
    {
        // Use a single buffer: base64 chars are ASCII (1:1 with UTF8 bytes),
        // and decoded data is always smaller than encoded, so we can decode in-place.
        int bufferLength = base64Chars.Length;
        Span<byte> buffer = bufferLength <= 256
            ? stackalloc byte[bufferLength]
            : new byte[bufferLength];

        Encoding.UTF8.GetBytes(base64Chars, buffer);

        OperationStatus status = Base64.DecodeFromUtf8InPlace(buffer, out int bytesWritten);
        if (status != OperationStatus.Done)
        {
            result = null;
            return false;
        }

        result = Encoding.UTF8.GetString(buffer[..bytesWritten]);
        return true;
    }
#endif
}
