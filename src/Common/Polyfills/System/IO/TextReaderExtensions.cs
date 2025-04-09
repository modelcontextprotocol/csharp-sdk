namespace System.IO;

internal static class TextReaderExtensions
{
    public static Task<string> ReadLineAsync(this TextReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return reader.ReadLineAsync();
    }
}