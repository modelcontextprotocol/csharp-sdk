using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a server MCP transport implemented around a TCP connection.
/// </summary>
public class TcpServerTransport : TransportBase, ITransport
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly ILogger _logger;
    private readonly TcpListener _tcpListener;
    private readonly string _endpointName;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly Task _readLoopCompleted;
    private NetworkStream? _networkStream;
    private StreamReader? _inputReader;
    private Stream? _outputStream;
    private int _disposed = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpServerTransport"/> class.
    /// </summary>
    /// <param name="port">The port to listen on.</param>
    /// <param name="serverName">Optional name of the server, used for diagnostic purposes, like logging.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    public TcpServerTransport(int port, string? serverName = null, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        _tcpListener = new TcpListener(IPAddress.Any, port);
        _tcpListener.Start();

        _endpointName = serverName is not null ? $"Server (TCP) ({serverName})" : "Server (TCP)";
        _readLoopCompleted = Task.Run(AcceptAndReadMessagesAsync, _shutdownCts.Token);
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.TransportNotConnected(_endpointName);
            throw new McpTransportException("Transport is not connected");
        }

        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            _logger.TransportSendingMessage(_endpointName, id);

            await JsonSerializer.SerializeAsync(_outputStream!, message, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)), cancellationToken).ConfigureAwait(false);
            await _outputStream!.WriteAsync(s_newlineBytes, cancellationToken).ConfigureAwait(false);
            await _outputStream!.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.TransportSentMessage(_endpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(_endpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    private async Task AcceptAndReadMessagesAsync()
    {
        CancellationToken shutdownToken = _shutdownCts.Token;
        try
        {
            _logger.TransportEnteringReadMessagesLoop(_endpointName);

            while (!shutdownToken.IsCancellationRequested)
            {
                _logger.TransportReadingMessages(_endpointName);

                var client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                _networkStream = client.GetStream();
                _inputReader = new StreamReader(_networkStream, Encoding.UTF8);
                _outputStream = _networkStream;

                SetConnected(true);
                _logger.TransportAlreadyConnected(_endpointName);

                while (!shutdownToken.IsCancellationRequested)
                {
                    var line = await _inputReader.ReadLineAsync(shutdownToken).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        if (line is null)
                        {
                            _logger.TransportEndOfStream(_endpointName);
                            break;
                        }

                        continue;
                    }

                    _logger.TransportReceivedMessage(_endpointName, line);

                    try
                    {
                        if (JsonSerializer.Deserialize(line, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage))) is IJsonRpcMessage message)
                        {
                            string messageId = "(no id)";
                            if (message is IJsonRpcMessageWithId messageWithId)
                            {
                                messageId = messageWithId.Id.ToString();
                            }
                            _logger.TransportReceivedMessageParsed(_endpointName, messageId);

                            await WriteMessageAsync(message, shutdownToken).ConfigureAwait(false);
                            _logger.TransportMessageWritten(_endpointName, messageId);
                        }
                        else
                        {
                            _logger.TransportMessageParseUnexpectedType(_endpointName, line);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.TransportMessageParseFailed(_endpointName, line, ex);
                        // Continue reading even if we fail to parse a message
                    }
                }

                client.Close();
                SetConnected(false);
            }

            _logger.TransportExitingReadMessagesLoop(_endpointName);
        }
        catch (OperationCanceledException)
        {
            _logger.TransportReadMessagesCancelled(_endpointName);
        }
        catch (Exception ex)
        {
            _logger.TransportReadMessagesFailed(_endpointName, ex);
        }
        finally
        {
            SetConnected(false);
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _logger.TransportCleaningUp(_endpointName);

            // Signal to the read loop to stop.
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            _shutdownCts.Dispose();

            // Dispose of network resources.
            _inputReader?.Dispose();
            _outputStream?.Dispose();
            _networkStream?.Dispose();
            _tcpListener.Stop();

            // Make sure the work has quiesced.
            try
            {
                _logger.TransportWaitingForReadTask(_endpointName);
                await _readLoopCompleted.ConfigureAwait(false);
                _logger.TransportReadTaskCleanedUp(_endpointName);
            }
            catch (TimeoutException)
            {
                _logger.TransportCleanupReadTaskTimeout(_endpointName);
            }
            catch (OperationCanceledException)
            {
                _logger.TransportCleanupReadTaskCancelled(_endpointName);
            }
            catch (Exception ex)
            {
                _logger.TransportCleanupReadTaskFailed(_endpointName, ex);
            }
        }
        finally
        {
            SetConnected(false);
            _logger.TransportCleanedUp(_endpointName);
        }

        GC.SuppressFinalize(this);
    }
}