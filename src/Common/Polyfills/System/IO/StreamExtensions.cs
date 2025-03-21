using ModelContextProtocol.Utils;
using System.Buffers;
using System.Runtime.InteropServices;

namespace System.IO;

internal static class StreamExtensions
{
    public static ValueTask WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        Throw.IfNull(stream);

        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return new ValueTask(stream.WriteAsync(segment.Array, segment.Offset, segment.Count, cancellationToken));
        }
        else
        {
            return WriteAsyncCore(stream, buffer, cancellationToken);

            static async ValueTask WriteAsyncCore(Stream stream, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    buffer.Span.CopyTo(array);
                    await stream.WriteAsync(array, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(array);
                }
            }
        }
    }
}