#if NET
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>
/// Couples an existing Streamable HTTP session transport to the lifetime of its stdio child process.
/// </summary>
internal sealed class HttpOverStdioClientSessionTransport : TransportBase
{
    private readonly ITransport _innerTransport;
    private readonly StdioClientProcess _process;
    private readonly HttpClient _resourceHttpClient;
    private readonly HttpClient? _defaultOAuthBackchannel;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _cleanupLock = new();

    private Task? _cleanupTask;
    private Exception? _cleanupError;
    private bool _disposeRequested;

    internal HttpOverStdioClientSessionTransport(
        string name,
        ITransport innerTransport,
        StdioClientProcess process,
        HttpClient resourceHttpClient,
        HttpClient? defaultOAuthBackchannel,
        ILoggerFactory? loggerFactory)
        : base(name, loggerFactory)
    {
        _innerTransport = innerTransport;
        _process = process;
        _resourceHttpClient = resourceHttpClient;
        _defaultOAuthBackchannel = defaultOAuthBackchannel;

        SetConnected();
        _ = ForwardMessagesAsync();
        _ = MonitorProcessAsync();
    }

    /// <inheritdoc />
    public override string? SessionId
    {
        get => _innerTransport.SessionId;
        protected set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override async Task SendMessageAsync(
        JsonRpcMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _innerTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (await _process.GetUnexpectedExitExceptionAsync().ConfigureAwait(false) is { } processError)
            {
                ClientTransportClosedException closedException = CreateClosedException(processError);
                _ = EnsureCleanupAsync(processError);
                throw closedException;
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async override ValueTask DisposeAsync()
    {
        await EnsureCleanupAsync(error: null, disposeRequested: true).ConfigureAwait(false);
    }

    private async Task ForwardMessagesAsync()
    {
        Exception? error = null;
        try
        {
            await foreach (JsonRpcMessage message in
                _innerTransport.MessageReader.ReadAllAsync(_shutdownCts.Token).ConfigureAwait(false))
            {
                await WriteMessageAsync(message, _shutdownCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = GetUnderlyingError(ex);
        }
        finally
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                await EnsureCleanupAsync(error).ConfigureAwait(false);
            }
        }
    }

    private async Task MonitorProcessAsync()
    {
        try
        {
            await _process.WaitForExitAsync(_shutdownCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            await EnsureCleanupAsync(ex).ConfigureAwait(false);
            return;
        }

        if (!_shutdownCts.IsCancellationRequested)
        {
            Exception error =
                await _process.GetUnexpectedExitExceptionAsync().ConfigureAwait(false) ??
                new IOException("The HTTP-over-stdio server process exited unexpectedly.");
            await EnsureCleanupAsync(error).ConfigureAwait(false);
        }
    }

    private Task EnsureCleanupAsync(Exception? error, bool disposeRequested = false)
    {
        lock (_cleanupLock)
        {
            _disposeRequested |= disposeRequested;
            _cleanupError ??= error;
            return _cleanupTask ??= CleanupAsync();
        }
    }

    private async Task CleanupAsync()
    {
        bool disposeRequested;
        Exception? error;
        lock (_cleanupLock)
        {
            disposeRequested = _disposeRequested;
            error = _cleanupError;
        }

        if (!disposeRequested && error is null)
        {
            error =
                await _process.GetUnexpectedExitExceptionAsync().ConfigureAwait(false) ??
                new IOException("The underlying Streamable HTTP transport closed unexpectedly.");
            RecordCleanupError(error);
        }

        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _innerTransport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordCleanupError(ex);
            LogTransportShutdownFailed(Name, ex);
        }

        _resourceHttpClient.Dispose();
        _defaultOAuthBackchannel?.Dispose();

        try
        {
            _process.CloseStandardInput();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
        }
        catch (IOException) when (_process.HasExited)
        {
        }
        catch (Exception ex)
        {
            RecordCleanupError(ex);
            LogTransportShutdownFailed(Name, ex);
        }

        await _process.WaitForExitWithinTimeoutAsync().ConfigureAwait(false);

        StdioClientCompletionDetails? details = null;
        try
        {
            _process.Dispose(
                processRunning: true,
                beforeDispose: () =>
                {
                    lock (_cleanupLock)
                    {
                        details = _process.BuildCompletionDetails(
                            _disposeRequested ? null : GetUnderlyingError(_cleanupError));
                    }
                });
        }
        catch (Exception ex)
        {
            RecordCleanupError(ex);
            LogTransportShutdownFailed(Name, ex);
        }

        lock (_cleanupLock)
        {
            details ??= new StdioClientCompletionDetails
            {
                Exception = _disposeRequested ? null : GetUnderlyingError(_cleanupError),
                ProcessId = _process.ProcessId,
            };
        }

        SetDisconnected(new ClientTransportClosedException(details));
    }

    private void RecordCleanupError(Exception error)
    {
        lock (_cleanupLock)
        {
            _cleanupError ??= GetUnderlyingError(error);
        }
    }

    private ClientTransportClosedException CreateClosedException(Exception error) =>
        new(_process.BuildCompletionDetails(GetUnderlyingError(error)));

    private static Exception? GetUnderlyingError(Exception? error) =>
        error is ClientTransportClosedException transportClosed
            ? transportClosed.Details.Exception ?? transportClosed
            : error;
}
#endif
