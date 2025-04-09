using ModelContextProtocol.Utils;
using System.Runtime.InteropServices;

namespace System.IO;

internal static class TextWriterExtensions
{
    public static async Task FlushAsync(this TextWriter writer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.FlushAsync();
    }
}