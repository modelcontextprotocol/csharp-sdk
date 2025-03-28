using Microsoft.Extensions.Logging;
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
public sealed class StdioServerTransport : TransportBase, IServerTransport
{
    private static readonly byte[] s_newlineBytes = "\n"u8.ToArray();

    private readonly string _serverName;
    private readonly ILogger _logger;

    private readonly JsonSerializerOptions _jsonOptions = McpJsonUtilities.DefaultOptions;
    private readonly TextReader _stdInReader;
    private readonly Stream _stdOutStream;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _readTask;
    private CancellationTokenSource? _shutdownCts;

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
    public StdioServerTransport(McpServerOptions serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions), loggerFactory)
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
    public StdioServerTransport(IOptions<McpServerOptions> serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions.Value), loggerFactory)
    {
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
    public StdioServerTransport(string serverName, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        Throw.IfNull(serverName);
        
        _serverName = serverName;
        _logger = (ILogger?)loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        
        // Get raw console streams and wrap them with UTF-8 encoding
        _stdInReader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        _stdOutStream = new BufferedStream(Console.OpenStandardOutput());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class with explicit input/output streams.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="stdinStream">The input TextReader to use.</param>
    /// <param name="stdoutStream">The output TextWriter to use.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This constructor is useful for testing scenarios where you want to redirect input/output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(string serverName, Stream stdinStream, Stream stdoutStream, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        Throw.IfNull(serverName);
        Throw.IfNull(stdinStream);
        Throw.IfNull(stdoutStream);
        
        _serverName = serverName;
        _logger = (ILogger?)loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        
        _stdInReader = new StreamReader(stdinStream, Encoding.UTF8);
        _stdOutStream = stdoutStream;
    }

    /// <inheritdoc/>
    public Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting StdioServerTransport listener for {EndpointName}", EndpointName);
        
        _shutdownCts = new CancellationTokenSource();

        _readTask = Task.Run(async () => await ReadMessagesAsync(_shutdownCts.Token).ConfigureAwait(false), CancellationToken.None);

        SetConnected(true);
        _logger.LogDebug("StdioServerTransport now connected for {EndpointName}", EndpointName);

        return Task.CompletedTask;
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

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.TransportEnteringReadMessagesLoop(EndpointName);

            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.TransportWaitingForMessage(EndpointName);

                var reader = _stdInReader;
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    _logger.TransportEndOfStream(EndpointName);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                _logger.TransportReceivedMessage(EndpointName, line);
                _logger.TransportMessageBytesUtf8(EndpointName, line);

                try
                {
                    var message = JsonSerializer.Deserialize(line, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
                    if (message != null)
                    {
                        string messageId = "(no id)";
                        if (message is IJsonRpcMessageWithId messageWithId)
                        {
                            messageId = messageWithId.Id.ToString();
                        }
                        _logger.TransportReceivedMessageParsed(EndpointName, messageId);
                        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
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
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.TransportReadMessagesFailed(EndpointName, ex);
        }
        finally
        {
            await CleanupAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        _logger.TransportCleaningUp(EndpointName);

        if (_shutdownCts is { } shutdownCts)
        {
            await shutdownCts.CancelAsync().ConfigureAwait(false);
            shutdownCts.Dispose();

            _shutdownCts = null;
        }

        if (_readTask is { } readTask)
        {
            try
            {
                _logger.TransportWaitingForReadTask(EndpointName);
                await readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.TransportCleanupReadTaskTimeout(EndpointName);
                // Continue with cleanup
            }
            catch (OperationCanceledException)
            {
                _logger.TransportCleanupReadTaskCancelled(EndpointName);
                // Ignore cancellation
            }
            catch (Exception ex)
            {
                _logger.TransportCleanupReadTaskFailed(EndpointName, ex);
            }
            finally
            {
                _logger.TransportReadTaskCleanedUp(EndpointName);
                _readTask = null;
            }
        }

        _stdInReader?.Dispose();
        _stdOutStream?.Dispose();

        SetConnected(false);
        _logger.TransportCleanedUp(EndpointName);
    }

    /// <summary>Validates the <paramref name="serverOptions"/> and extracts from it the server name to use.</summary>
    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);
        Throw.IfNull(serverOptions.ServerInfo);

        return serverOptions.ServerInfo.Name;
    }
}