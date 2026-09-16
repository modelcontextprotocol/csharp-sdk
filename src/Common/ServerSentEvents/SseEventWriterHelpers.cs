// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Copied from https://github.com/dotnet/runtime/blob/dcbf3413c5f7ae431a68fd0d3f09af095b525887/src/libraries/System.Net.ServerSentEvents/src/System/Net/ServerSentEvents/Helpers.cs

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace System.Net.ServerSentEvents;

internal static class SseEventWriterHelpers
{
    public static void WriteUtf8Number(this IBufferWriter<byte> writer, long value)
    {
#if NET
        const int MaxDecimalDigits = 20;
        Span<byte> buffer = writer.GetSpan(MaxDecimalDigits);
        Debug.Assert(MaxDecimalDigits <= buffer.Length);

        bool success = value.TryFormat(buffer, out int bytesWritten, provider: CultureInfo.InvariantCulture);
        Debug.Assert(success);
        writer.Advance(bytesWritten);
#else
        writer.WriteUtf8String(value.ToString(CultureInfo.InvariantCulture));
#endif
    }

    public static void WriteUtf8String(this IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        Span<byte> buffer = writer.GetSpan(value.Length);
        Debug.Assert(value.Length <= buffer.Length);
        value.CopyTo(buffer);
        writer.Advance(value.Length);
    }

    public static void WriteUtf8String(this IBufferWriter<byte> writer, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);
        Span<byte> buffer = writer.GetSpan(maxByteCount);
        Debug.Assert(buffer.Length >= maxByteCount);

        int bytesWritten = Encoding.UTF8.GetBytes(value, buffer);
        writer.Advance(bytesWritten);
    }

    public static bool ContainsLineBreaks(this ReadOnlySpan<char> text) =>
        text.IndexOfAny('\r', '\n') >= 0;

    public static bool ContainsLineBreaks(this string? text) =>
        text is not null && text.AsSpan().ContainsLineBreaks();
}
