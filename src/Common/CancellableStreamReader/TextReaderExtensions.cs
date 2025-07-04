namespace System.IO;

internal static class TextReaderExtensions
{
    public static ValueTask<string> ReadLineAsync(this TextReader reader, CancellationToken cancellationToken)
    {
        if (reader is CancellableStreamReader cancellableReader)
        {
            return cancellableReader.ReadLineAsync(cancellationToken)!;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<string>(reader.ReadLineAsync());
    }
}