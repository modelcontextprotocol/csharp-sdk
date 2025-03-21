namespace System.IO;

internal static class TextReaderExtensions
{
    public static Task<string> ReadLineAsync(this TextReader reader, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string>(cancellationToken);
        }

        return reader.ReadLineAsync();
    }

    public static Task<string> ReadToEndAsync(this TextReader reader, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string>(cancellationToken);
        }

        return reader.ReadToEndAsync();
    }
}