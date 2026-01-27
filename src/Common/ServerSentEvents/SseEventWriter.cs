// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Based on https://github.com/dotnet/runtime/blob/dcbf3413c5f7ae431a68fd0d3f09af095b525887/src/libraries/System.Net.ServerSentEvents/src/System/Net/ServerSentEvents/SseFormatter.cs

using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.ServerSentEvents;

/// <summary>
/// Provides methods for writing SSE events to a stream.
/// </summary>
internal sealed class SseEventWriter : IDisposable
{
    private static readonly byte[] s_newLine = "\n"u8.ToArray();

    private readonly Stream _destination;
    private readonly PooledByteBufferWriter _bufferWriter = new();
    private readonly PooledByteBufferWriter _userDataBufferWriter = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SseEventWriter"/> class with the specified destination stream and item formatter.
    /// </summary>
    /// <param name="destination">The stream to write SSE events to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public SseEventWriter(Stream destination)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    /// <summary>
    /// Writes an SSE item to the destination stream.
    /// </summary>
    /// <param name="item">The SSE item to write.</param>
    /// <param name="itemFormatter"></param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public async ValueTask WriteAsync<T>(SseItem<T> item, Action<SseItem<T>, IBufferWriter<byte>> itemFormatter, CancellationToken cancellationToken = default)
    {
        itemFormatter(item, _userDataBufferWriter);

        FormatSseEvent(
            _bufferWriter,
            eventType: item.EventType,
            data: _userDataBufferWriter.WrittenMemory.Span,
            eventId: item.EventId,
            reconnectionInterval: item.ReconnectionInterval);

        await _destination.WriteAsync(_bufferWriter.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _destination.FlushAsync(cancellationToken).ConfigureAwait(false);

        _userDataBufferWriter.Reset();
        _bufferWriter.Reset();
    }

    private static void FormatSseEvent(
        IBufferWriter<byte> bufferWriter,
        string? eventType,
        ReadOnlySpan<byte> data,
        string? eventId,
        TimeSpan? reconnectionInterval)
    {
        if (eventType is not null)
        {
            Debug.Assert(!eventType.ContainsLineBreaks());

            bufferWriter.WriteUtf8String("event: "u8);
            bufferWriter.WriteUtf8String(eventType);
            bufferWriter.WriteUtf8String(s_newLine);
        }

        WriteLinesWithPrefix(bufferWriter, prefix: "data: "u8, data);
        bufferWriter.Write(s_newLine);

        if (eventId is not null)
        {
            Debug.Assert(!eventId.ContainsLineBreaks());

            bufferWriter.WriteUtf8String("id: "u8);
            bufferWriter.WriteUtf8String(eventId);
            bufferWriter.WriteUtf8String(s_newLine);
        }

        if (reconnectionInterval is { } retry)
        {
            Debug.Assert(retry >= TimeSpan.Zero);

            bufferWriter.WriteUtf8String("retry: "u8);
            bufferWriter.WriteUtf8Number((long)retry.TotalMilliseconds);
            bufferWriter.WriteUtf8String(s_newLine);
        }

        bufferWriter.WriteUtf8String(s_newLine);
    }

    private static void WriteLinesWithPrefix(IBufferWriter<byte> writer, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> data)
    {
        // Writes a potentially multi-line string, prefixing each line with the given prefix.
        // Both \n and \r\n sequences are normalized to \n.

        while (true)
        {
            writer.WriteUtf8String(prefix);

            int i = data.IndexOfAny((byte)'\r', (byte)'\n');
            if (i < 0)
            {
                writer.WriteUtf8String(data);
                return;
            }

            int lineLength = i;
            if (data[i++] == '\r' && i < data.Length && data[i] == '\n')
            {
                i++;
            }

            ReadOnlySpan<byte> nextLine = data.Slice(0, lineLength);
            data = data.Slice(i);

            writer.WriteUtf8String(nextLine);
            writer.WriteUtf8String(s_newLine);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _bufferWriter.Dispose();
        _userDataBufferWriter.Dispose();
    }
}
