using ModelContextProtocol;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

#if !NET
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

    public static ValueTask<int> ReadAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Throw.IfNull(stream);
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return new ValueTask<int>(stream.ReadAsync(segment.Array, segment.Offset, segment.Count, cancellationToken));
        }
        else
        {
            return ReadAsyncCore(stream, buffer, cancellationToken);
            static async ValueTask<int> ReadAsyncCore(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
            {
                byte[] array = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    int bytesRead = await stream.ReadAsync(array, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    array.AsSpan(0, bytesRead).CopyTo(buffer.Span);
                    return bytesRead;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(array);
                }
            }
        }
    }
}
#endif