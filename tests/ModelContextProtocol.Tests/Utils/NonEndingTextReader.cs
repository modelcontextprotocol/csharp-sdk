using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// A special TextReader that can be used in tests to simulate stdin without reaching EOF.
/// Particularly useful for testing transports that need to maintain an active connection.
/// </summary>
public class NonEndingTextReader(CancellationToken cancellationToken = default) : TextReader
{
    private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>();

    public override Task<string?> ReadLineAsync()
    {
        return ReadLineAsync(cancellationToken).AsTask();
    }

    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }

    public void WriteLine(string line)
    {
        _channel.Writer.TryWrite(line);
    }

    public override void Close()
    {
        _channel.Writer.Complete();
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _channel.Writer.Complete();
        }

        base.Dispose(disposing);
    }
}
