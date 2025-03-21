using ModelContextProtocol.Utils;
using System.Runtime.InteropServices;

namespace System.IO;

internal static class TextWriterExtensions
{
    public static async Task WriteLineAsync(this TextWriter writer, ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        Throw.IfNull(writer);

        if (value.IsEmpty)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (MemoryMarshal.TryGetString(value, out string str, out int start, out int length) &&
            start == 0 && length == str.Length)
        {
            await writer.WriteLineAsync(str).ConfigureAwait(false);
        }
        else if (MemoryMarshal.TryGetArray(value, out ArraySegment<char> array) &&
            array.Array is not null && array.Offset == 0 && array.Count == array.Array.Length)
        {
            await writer.WriteLineAsync(array.Array).ConfigureAwait(false);
        }
        else
        {
            await writer.WriteLineAsync(value.ToArray()).ConfigureAwait(false);
        }
    }

    public static async Task FlushAsync(this TextWriter writer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.FlushAsync();
    }
}