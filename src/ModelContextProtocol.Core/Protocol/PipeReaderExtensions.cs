using System.Buffers;
using System.IO.Pipelines;

namespace ModelContextProtocol.Protocol;

/// <summary>Internal helper for reading newline-delimited UTF-8 lines from a <see cref="PipeReader"/>.</summary>
internal static class PipeReaderExtensions
{
    /// <summary>
    /// Reads newline-delimited lines from <paramref name="reader"/>, invoking
    /// <paramref name="processLine"/> for each non-empty line, until the reader signals completion.
    /// </summary>
    internal static async Task ReadLinesAsync(
        this PipeReader reader,
        Func<ReadOnlySequence<byte>, CancellationToken, Task> processLine,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            SequencePosition? position;
            while ((position = buffer.PositionOf((byte)'\n')) != null)
            {
                ReadOnlySequence<byte> line = buffer.Slice(0, position.Value);

                // Trim trailing \r for Windows-style CRLF line endings.
                if (EndsWithCarriageReturn(line))
                {
                    line = line.Slice(0, line.Length - 1);
                }

                if (!line.IsEmpty)
                {
                    await processLine(line, cancellationToken).ConfigureAwait(false);
                }

                // Advance past the '\n'.
                buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }
    }

    private static bool EndsWithCarriageReturn(in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment)
        {
            ReadOnlySpan<byte> span = sequence.First.Span;
            return span.Length > 0 && span[span.Length - 1] == (byte)'\r';
        }

        // Multi-segment: find the last non-empty segment to check its last byte.
        ReadOnlyMemory<byte> last = default;
        foreach (ReadOnlyMemory<byte> segment in sequence)
        {
            if (!segment.IsEmpty)
            {
                last = segment;
            }
        }

        return !last.IsEmpty && last.Span[last.Length - 1] == (byte)'\r';
    }
}
