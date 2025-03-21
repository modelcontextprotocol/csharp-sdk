using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Configuration;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Utils;
using ModelContextProtocol.Utils.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Implements the MCP transport protocol over standard input/output streams.
/// </summary>
public sealed class StdioClientTransport : TransportBase, IClientTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly McpServerConfig _serverConfig;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private Process? _process;
    private Task? _readTask;
    private CancellationTokenSource? _shutdownCts;
    private bool _processStarted;

    private string EndpointName => $"Client (stdio) for ({_serverConfig.Id}: {_serverConfig.Name})";

    /// <summary>
    /// Initializes a new instance of the StdioTransport class.
    /// </summary>
    /// <param name="options">Configuration options for the transport.</param>
    /// <param name="serverConfig">The server configuration for the transport.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public StdioClientTransport(StdioClientTransportOptions options, McpServerConfig serverConfig, ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {
        Throw.IfNull(options);
        Throw.IfNull(serverConfig);

        _options = options;
        _serverConfig = serverConfig;
        _logger = (ILogger?)loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        _jsonOptions = McpJsonUtilities.DefaultOptions;
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.TransportAlreadyConnected(EndpointName);
            throw new McpTransportException("Transport is already connected");
        }

        try
        {
            _logger.TransportConnecting(EndpointName);

            _shutdownCts = new CancellationTokenSource();

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory,
            };

            if (!string.IsNullOrWhiteSpace(_options.Arguments))
            {
                startInfo.Arguments = _options.Arguments;
            }

            if (_options.EnvironmentVariables != null)
            {
                foreach (var entry in _options.EnvironmentVariables)
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }

            _logger.CreateProcessForTransport(EndpointName, _options.Command,
                startInfo.Arguments, string.Join(", ", startInfo.Environment.Select(kvp => kvp.Key + "=" + kvp.Value)),
                startInfo.WorkingDirectory, _options.ShutdownTimeout.ToString());

            _process = new Process { StartInfo = startInfo };

            // Set up error logging
            _process.ErrorDataReceived += (sender, args) => _logger.TransportError(EndpointName, args.Data ?? "(no data)");

            if (!_process.Start())
            {
                _logger.TransportProcessStartFailed(EndpointName);
                throw new McpTransportException("Failed to start MCP server process");
            }
            _logger.TransportProcessStarted(EndpointName, _process.Id);
            _processStarted = true;
            _process.BeginErrorReadLine();

            // Start reading messages in the background
            _readTask = Task.Run(() => ReadMessagesAsync(_shutdownCts.Token), CancellationToken.None);
            _logger.TransportReadingMessages(EndpointName);

            SetConnected(true);
        }
        catch (Exception ex)
        {
            _logger.TransportConnectFailed(EndpointName, ex);
            await CleanupAsync(cancellationToken).ConfigureAwait(false);
            throw new McpTransportException("Failed to connect transport", ex);
        }
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _process?.HasExited == true)
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
            var json = JsonSerializer.Serialize(message, _jsonOptions.GetTypeInfo<IJsonRpcMessage>());
            _logger.TransportSendingMessage(EndpointName, id, json);

            // Write the message followed by a newline
            await _process!.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

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

            using var reader = _process!.StandardOutput;

            while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
            {
                _logger.TransportWaitingForMessage(EndpointName);
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

                await ProcessMessageAsync(line, cancellationToken).ConfigureAwait(false);
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

    private async Task ProcessMessageAsync(string line, CancellationToken cancellationToken)
    {
        try
        {                    
            line=line.Trim();//Fixes an error when the service prefixes nonprintable characters
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
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        _logger.TransportCleaningUp(EndpointName);
        if (_process != null && _processStarted && !_process.HasExited)
        {
            try
            {
                // Try to close stdin to signal the process to exit
                _logger.TransportClosingStdin(EndpointName);
                _process.StandardInput.Close();

                // Wait for the process to exit
                _logger.TransportWaitingForShutdown(EndpointName);

                // Kill the while process tree because the process may spawn child processes
                // and Node.js does not kill its children when it exits properly
                _process.KillTree(_options.ShutdownTimeout);
            }
            catch (Exception ex)
            {
                _logger.TransportShutdownFailed(EndpointName, ex);
            }

            _process.Dispose();
            _process = null;
        }

        if (_shutdownCts is { } shutdownCts)
        {
            await shutdownCts.CancelAsync().ConfigureAwait(false);
            shutdownCts.Dispose();
            _shutdownCts = null;
        }

        if (_readTask != null)
        {
            try
            {
                _logger.TransportWaitingForReadTask(EndpointName);
                await _readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
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
            _readTask = null;
            _logger.TransportReadTaskCleanedUp(EndpointName);
        }

        SetConnected(false);
        _logger.TransportCleanedUp(EndpointName);
    }

}
