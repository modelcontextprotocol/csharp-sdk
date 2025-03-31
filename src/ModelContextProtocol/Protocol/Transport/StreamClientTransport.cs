using Microsoft.Extensions.Logging;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a client MCP transport implemented around a pair of input/output streams.
/// </summary>
public sealed class StreamClientTransport : IClientTransport
{
    private readonly Stream _serverInput;
    private readonly Stream _serverOutput;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamClientTransport"/> class.
    /// </summary>
    /// <param name="serverInput">
    /// The stream representing the connected server's input. 
    /// Writes to this stream will be sent to the server.
    /// </param>
    /// <param name="serverOutput">
    /// The stream representing the connected server's output.
    /// Reads from this stream will receive messages from the server.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public StreamClientTransport(
        Stream serverInput, Stream serverOutput, ILoggerFactory? loggerFactory = null)
    {
        Throw.IfNull(serverInput);
        Throw.IfNull(serverOutput);

        _serverInput = serverInput;
        _serverOutput = serverOutput;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITransport>(new StreamClientSessionTransport(
            new StreamWriter(_serverInput),
            new StreamReader(_serverOutput),
            "Client (stream)",
            _loggerFactory));
    }
}
