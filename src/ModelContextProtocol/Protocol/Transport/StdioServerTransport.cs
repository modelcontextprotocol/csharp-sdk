﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides an implementation of the MCP transport protocol over standard input/output streams.
/// </summary>
public sealed class StdioServerTransport : TransportBase, ITransport
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly string _serverName;
    private readonly ILogger _logger;

    private readonly JsonSerializerOptions _jsonOptions = McpJsonUtilities.DefaultOptions;
    private readonly TextReader _stdInReader;
    private readonly Stream _stdOutStream;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly Task _readLoopCompleted;
    private int _disposed = 0;

    private string EndpointName => $"Server (stdio) ({_serverName})";

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(IOptions<McpServerOptions> serverOptions, ILoggerFactory? loggerFactory = null)
        : this(serverOptions?.Value!, loggerFactory: loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(McpServerOptions serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions), loggerFactory: loggerFactory)
    {
    }

    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);
        Throw.IfNull(serverOptions.ServerInfo);
        Throw.IfNull(serverOptions.ServerInfo.Name);
        return serverOptions.ServerInfo.Name;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(string serverName, ILoggerFactory? loggerFactory)
        : this(serverName, stdinStream: null, stdoutStream: null, loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class with explicit input/output streams.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="stdinStream">The input <see cref="Stream"/> to use as standard input. If <see langword="null"/>, <see cref="Console.In"/> will be used.</param>
    /// <param name="stdoutStream">The output <see cref="Stream"/> to use as standard output. If <see langword="null"/>, <see cref="Console.Out"/> will be used.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This constructor is useful for testing scenarios where you want to redirect input/output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(string serverName, Stream? stdinStream = null, Stream? stdoutStream = null, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        Throw.IfNull(serverName);
        
        _serverName = serverName;
        _logger = (ILogger?)loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;

        _stdInReader = new StreamReader(stdinStream ?? Console.OpenStandardInput(), Encoding.UTF8);
        _stdOutStream = stdoutStream ?? new BufferedStream(Console.OpenStandardOutput());

        SetConnected(true);
        _readLoopCompleted = Task.Run(ReadMessagesAsync, _shutdownCts.Token);
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        using var _ = await _sendLock.LockAsync(cancellationToken).ConfigureAwait(false);

        if (!IsConnected)
        {
            _logger.TransportNotConnected(EndpointName);
            throw new McpTransportException("Transport is not connected");
        }

        string id = "(no id)";
        if (message is IJsonRpcMessageWithId messageWithId)
        {
            id = messageWithId.Id.ToString();
        }

        try
        {
            _logger.TransportSendingMessage(EndpointName, id);

            await JsonSerializer.SerializeAsync(_stdOutStream, message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>(), cancellationToken).ConfigureAwait(false);
            await _stdOutStream.WriteAsync(s_newlineBytes, cancellationToken).ConfigureAwait(false);
            await _stdOutStream.FlushAsync(cancellationToken).ConfigureAwait(false);;

            _logger.TransportSentMessage(EndpointName, id);
        }
        catch (Exception ex)
        {
            _logger.TransportSendFailed(EndpointName, id, ex);
            throw new McpTransportException("Failed to send message", ex);
        }
    }

    private async Task ReadMessagesAsync()
    {
        CancellationToken shutdownToken = _shutdownCts.Token;
        try
        {
            _logger.TransportEnteringReadMessagesLoop(EndpointName);

            while (!shutdownToken.IsCancellationRequested)
            {
                _logger.TransportWaitingForMessage(EndpointName);

                var line = await _stdInReader.ReadLineAsync(shutdownToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (line is null)
                    {
                        _logger.TransportEndOfStream(EndpointName);
                        break;
                    }

                    continue;
                }

                _logger.TransportReceivedMessage(EndpointName, line);
                _logger.TransportMessageBytesUtf8(EndpointName, line);

                try
                {
                    if (JsonSerializer.Deserialize(line, _jsonOptions.GetTypeInfo<IJsonRpcMessage>()) is { } message)
                    {
                        string messageId = "(no id)";
                        if (message is IJsonRpcMessageWithId messageWithId)
                        {
                            messageId = messageWithId.Id.ToString();
                        }
                        _logger.TransportReceivedMessageParsed(EndpointName, messageId);

                        await WriteMessageAsync(message, shutdownToken).ConfigureAwait(false);
                        _logger.TransportMessageWritten(EndpointName, messageId);
                    }
                    else
                    {
                        _logger.TransportMessageParseUnexpectedType(EndpointName, line);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.TransportMessageParseFailed(EndpointName, line, ex);
                    // Continue reading even if we fail to parse a message
                }
            }

            _logger.TransportExitingReadMessagesLoop(EndpointName);
        }
        catch (OperationCanceledException)
        {
            _logger.TransportReadMessagesCancelled(EndpointName);
        }
        catch (Exception ex)
        {
            _logger.TransportReadMessagesFailed(EndpointName, ex);
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
            _logger.TransportCleaningUp(EndpointName);

            // Signal to the stdin reading loop to stop.
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            _shutdownCts.Dispose();

            // Dispose of stdin/out. Cancellation may not be able to wake up operations
            // synchronously blocked in a syscall; we need to forcefully close the handle / file descriptor.
            _stdInReader?.Dispose();
            _stdOutStream?.Dispose();

            // Make sure the work has quiesced.
            try
            {
                _logger.TransportWaitingForReadTask(EndpointName);
                await _readLoopCompleted.ConfigureAwait(false);
                _logger.TransportReadTaskCleanedUp(EndpointName);
            }
            catch (TimeoutException)
            {
                _logger.TransportCleanupReadTaskTimeout(EndpointName);
            }
            catch (OperationCanceledException)
            {
                _logger.TransportCleanupReadTaskCancelled(EndpointName);
            }
            catch (Exception ex)
            {
                _logger.TransportCleanupReadTaskFailed(EndpointName, ex);
            }
        }
        finally
        {
            SetConnected(false);
            _logger.TransportCleanedUp(EndpointName);
        }
    }
}
