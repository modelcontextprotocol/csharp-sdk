namespace ModelContextProtocol.AspNetCore;

internal sealed class HttpOverStdioStreams
{
    public HttpOverStdioStreams()
        : this(Console.OpenStandardInput(), Console.OpenStandardOutput())
    {
    }

    public HttpOverStdioStreams(Stream input, Stream output)
    {
        Input = input;
        Output = output;
    }

    public Stream Input { get; }

    public Stream Output { get; }
}
