using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using System.Diagnostics;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a stdio-based session transport.</summary>
internal sealed class StdioClientSessionTransport : StreamClientSessionTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly Process _process;
    private readonly Queue<string> _stderrRollingLog;
    private readonly DataReceivedEventHandler _errorHandler;
    private readonly Task _stderrCompleted;
    private readonly TaskCompletionSource<bool> _cleanupCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _cleanedUp = 0;
    private readonly int? _processId;

    public StdioClientSessionTransport(StdioClientTransportOptions options, Process process, string endpointName, Queue<string> stderrRollingLog, DataReceivedEventHandler errorHandler, Task stderrCompleted, ILoggerFactory? loggerFactory) :
        base(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, encoding: null, endpointName, loggerFactory, startReading: false)
    {
        _options = options;
        _process = process;
        _stderrRollingLog = stderrRollingLog;
        _errorHandler = errorHandler;
        _stderrCompleted = stderrCompleted;
        try { _processId = process.Id; } catch { }
        StartReading();
    }

    /// <inheritdoc/>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await base.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // We failed to send due to an I/O error. If the server process has exited, which is then very likely the cause
            // for the I/O error, we should throw an exception for that instead.
            if (await GetUnexpectedExitExceptionAsync().ConfigureAwait(false) is Exception processExitException)
            {
                throw processExitException;
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        await _cleanupCompleted.Task.ConfigureAwait(false);
        SetDisconnected();
    }

    /// <inheritdoc/>
    protected override async ValueTask CleanupAsync(Exception? error = null, CancellationToken cancellationToken = default)
    {
        // The read loop can re-enter cleanup while disposal is waiting for process exit.
        if (Interlocked.Exchange(ref _cleanedUp, 1) != 0)
        {
            return;
        }

        try
        {
            await CleanupCoreAsync(error, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cleanupCompleted.TrySetResult(true);
        }
    }

    private async ValueTask CleanupCoreAsync(Exception? error, CancellationToken cancellationToken)
    {
        Stopwatch shutdownStopwatch = Stopwatch.StartNew();
        Task? readTask = ReadMessagesTask;

        // We've not yet forcefully terminated the server. If it's already shut down, something went wrong,
        // so create an exception with details about that.
        bool waitedForExit = false;
        if (error is null)
        {
            error = await GetUnexpectedExitExceptionAsync().ConfigureAwait(false);
            waitedForExit = error is not null;
        }

        BeginShutdownDrain();

        // Closing the server's stdin is the portable graceful-shutdown signal for stdio transports.
        // Do this before waiting so a well-behaved server can finish its own cleanup and exit.
        try
        {
            _process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            LogStandardInputCloseFailed(Name, ex);
        }

        // Wait within the shutdown timeout for pending ErrorDataReceived events.
        // GetUnexpectedExitExceptionAsync already waited if the process had exited.
        if (!waitedForExit)
        {
            await WaitForProcessExitAsync().ConfigureAwait(false);
        }

        if (readTask is not null)
        {
            TimeSpan remainingShutdownTime = _options.ShutdownTimeout == Timeout.InfiniteTimeSpan ?
                Timeout.InfiniteTimeSpan : _options.ShutdownTimeout - shutdownStopwatch.Elapsed;
            if (remainingShutdownTime > TimeSpan.Zero || remainingShutdownTime == Timeout.InfiniteTimeSpan)
            {
                Task completed = await Task.WhenAny(readTask, Task.Delay(remainingShutdownTime)).ConfigureAwait(false);
                if (completed == readTask)
                {
                    await readTask.ConfigureAwait(false);
                }
            }
        }

        CancelShutdown();

        // Detach the handler; a callback already in progress may outlive cleanup.
        _process.ErrorDataReceived -= _errorHandler;

        // Terminate the server process (or confirm it already exited), then build
        // and publish strongly-typed completion details while the process handle
        // is still valid so we can read the exit code.
        try
        {
            StdioClientTransport.DisposeProcess(
                _process,
                processRunning: true,
                _options.ShutdownTimeout,
                beforeDispose: () => SetDisconnected(new ClientTransportClosedException(BuildCompletionDetails(error))));
        }
        catch (Exception ex)
        {
            LogTransportShutdownFailed(Name, ex);
            SetDisconnected(new ClientTransportClosedException(BuildCompletionDetails(error)));
        }

        // And handle cleanup in the base type. SetDisconnected has already been
        // called above, so the base call is a no-op for disconnect state but
        // still performs other cleanup (cancelling the read task, etc.).
        await base.CleanupAsync(error, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Exception?> GetUnexpectedExitExceptionAsync()
    {
        if (!StdioClientTransport.HasExited(_process))
        {
            return null;
        }

        Debug.Assert(StdioClientTransport.HasExited(_process));

        await WaitForProcessExitAsync().ConfigureAwait(false);

        string errorMessage = "MCP server process exited unexpectedly";

        string? exitCode = null;
        try
        {
            exitCode = $" (exit code: {(uint)_process.ExitCode})";
        }
        catch { }

        lock (_stderrRollingLog)
        {
            if (_stderrRollingLog.Count > 0)
            {
                errorMessage =
                    $"{errorMessage}{exitCode}{Environment.NewLine}" +
                    $"Server's stderr tail:{Environment.NewLine}" +
                    $"{string.Join(Environment.NewLine, _stderrRollingLog)}";
            }
        }

        return new IOException(errorMessage);
    }

    /// <summary>
    /// Waits for process exit and pending <see cref="Process.ErrorDataReceived"/> events
    /// within <see cref="StdioClientTransportOptions.ShutdownTimeout"/>.
    /// </summary>
    /// <remarks>
    /// On .NET, <c>Process.WaitForExitAsync</c> also waits for asynchronous output readers
    /// to complete. On .NET Framework, <see cref="Process.WaitForExit(int)"/> does not
    /// wait for those events, so the stderr end-of-stream event is awaited separately.
    /// </remarks>
    private async ValueTask WaitForProcessExitAsync()
    {
        try
        {
#if NET
            using var timeoutCts = new CancellationTokenSource(_options.ShutdownTimeout);
            await _process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
#else
            Stopwatch stopwatch = Stopwatch.StartNew();
            if (_process.WaitForExit((int)_options.ShutdownTimeout.TotalMilliseconds))
            {
                if (_options.ShutdownTimeout == Timeout.InfiniteTimeSpan)
                {
                    await _stderrCompleted.ConfigureAwait(false);
                }
                else
                {
                    TimeSpan remaining = _options.ShutdownTimeout - stopwatch.Elapsed;
                    if (remaining > TimeSpan.Zero)
                    {
                        await _stderrCompleted.WaitAsync(remaining).ConfigureAwait(false);
                    }
                }
            }
#endif
        }
        catch { }
    }

    private StdioClientCompletionDetails BuildCompletionDetails(Exception? error)
    {
        StdioClientCompletionDetails details = new()
        {
            Exception = error,
            ProcessId = _processId,
        };

        try
        {
            if (StdioClientTransport.HasExited(_process))
            {
                details.ExitCode = _process.ExitCode;
            }
        }
        catch { }

        lock (_stderrRollingLog)
        {
            if (_stderrRollingLog.Count > 0)
            {
                details.StandardErrorTail = _stderrRollingLog.ToArray();
            }
        }

        return details;
    }
}
