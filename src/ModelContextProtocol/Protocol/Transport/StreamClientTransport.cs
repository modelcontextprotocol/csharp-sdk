using Microsoft.Extensions.Logging;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a client MCP transport implemented around a pair of input/output streams.
/// </summary>
/// <remarks>
/// This transport is useful for scenarios where you already have established streams for communication,
/// such as custom network protocols, pipe connections, or for testing purposes. It works with any
/// readable and writable streams that support JSON-RPC message exchange.
/// 
/// <para>
/// Unlike <see cref="StdioClientTransport"/> which manages process lifetime, or <see cref="SseClientTransport"/>
/// which manages HTTP connections, this transport only wraps existing streams and doesn't manage their lifecycle.
/// The caller is responsible for creating and disposing the underlying streams.
/// </para>
/// 
/// <example>
/// <code>
/// // Example using memory streams for testing
/// var inputStream = new MemoryStream();
/// var outputStream = new MemoryStream();
/// 
/// var transport = new StreamClientTransport(inputStream, outputStream);
/// var mcpClient = await McpClientFactory.CreateAsync(transport, serverConfig);
/// </code>
/// </example>
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
    /// <remarks>
    /// Both streams must be ready for reading and writing. The caller maintains ownership
    /// of the streams and is responsible for their disposal when communication is complete.
    /// The transport does not close or dispose these streams.
    /// </remarks>
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
    public string Name => $"in-memory-stream";


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
