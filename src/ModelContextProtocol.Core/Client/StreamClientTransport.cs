using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides an <see cref="IClientTransport"/> implemented around a pair of input/output streams.
/// </summary>
/// <remarks>
/// This transport is useful for scenarios where you already have established streams for communication,
/// such as custom network protocols, pipe connections, or for testing purposes. It works with any
/// readable and writable streams.
/// </remarks>
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
    /// <exception cref="ArgumentNullException"><paramref name="serverInput"/> or <paramref name="serverOutput"/> is <see langword="null"/>.</exception>
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
    public string Name => "in-memory-stream";

    /// <inheritdoc />
    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITransport>(new StreamClientSessionTransport(
            _serverInput,
            _serverOutput,
            encoding: null,
            "Client (stream)",
            _loggerFactory));
    }
}
