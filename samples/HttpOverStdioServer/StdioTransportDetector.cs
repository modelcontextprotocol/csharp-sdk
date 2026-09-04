using System.Buffers;
using System.IO.Pipelines;

namespace HttpOverStdioServer;

internal enum StdioTransportKind
{
    Legacy,
    Http2,
}

internal static class StdioTransportDetector
{
    private static readonly byte[] s_http2ConnectionPreface =
        "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    public static async ValueTask<StdioTransportKind> DetectAsync(
        PipeReader input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        while (true)
        {
            ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            SequencePosition examined = buffer.Start;
            StdioTransportKind? selectedTransport = null;

            try
            {
                if (result.IsCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (!buffer.IsEmpty)
                {
                    int compareLength = (int)Math.Min(buffer.Length, s_http2ConnectionPreface.Length);
                    var reader = new SequenceReader<byte>(buffer);

                    if (!reader.IsNext(s_http2ConnectionPreface.AsSpan(0, compareLength), advancePast: false))
                    {
                        selectedTransport = StdioTransportKind.Legacy;
                    }
                    else if (buffer.Length >= s_http2ConnectionPreface.Length)
                    {
                        selectedTransport = StdioTransportKind.Http2;
                    }
                    else if (result.IsCompleted)
                    {
                        selectedTransport = StdioTransportKind.Legacy;
                    }
                    else
                    {
                        examined = buffer.End;
                    }
                }
                else if (result.IsCompleted)
                {
                    selectedTransport = StdioTransportKind.Legacy;
                }
            }
            finally
            {
                // Detection must not consume bytes; the selected transport needs the complete original input.
                input.AdvanceTo(buffer.Start, examined);
            }

            if (selectedTransport is { } transport)
            {
                return transport;
            }
        }
    }
}
