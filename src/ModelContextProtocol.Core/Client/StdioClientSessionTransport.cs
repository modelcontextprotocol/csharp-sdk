using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Client;

/// <summary>Provides the client side of a stdio-based session transport.</summary>
internal sealed class StdioClientSessionTransport : StreamClientSessionTransport
{
    private readonly StdioClientProcess _process;
    private int _cleanedUp = 0;

    public StdioClientSessionTransport(StdioClientProcess process, string endpointName, ILoggerFactory? loggerFactory) :
        base(process.StandardInput, process.StandardOutput, encoding: null, endpointName, loggerFactory)
    {
        _process = process;
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
            if (await _process.GetUnexpectedExitExceptionAsync().ConfigureAwait(false) is Exception processExitException)
            {
                throw processExitException;
            }

            throw;
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask CleanupAsync(Exception? error = null, CancellationToken cancellationToken = default)
    {
        // Only run the full stdio cleanup once (handler detach, process kill, etc.).
        // If another call is already handling cleanup, cancel the shutdown token
        // to unblock it (e.g. if it's stuck in WaitForExitAsync) and let it
        // call SetDisconnected with full StdioClientCompletionDetails.
        if (Interlocked.Exchange(ref _cleanedUp, 1) != 0)
        {
            CancelShutdown();
            return;
        }

        // We've not yet forcefully terminated the server. If it's already shut down, something went wrong,
        // so create an exception with details about that.
        error ??= await _process.GetUnexpectedExitExceptionAsync().ConfigureAwait(false);

        // Ensure all pending ErrorDataReceived events are drained before detaching
        // the handler. GetUnexpectedExitExceptionAsync does this when HasExited is
        // true, but there is a narrow window on Linux where the process has closed
        // stdout (causing EOF in ReadMessagesAsync) yet hasn't been fully reaped,
        // so HasExited returns false and the drain is skipped. An unconditional
        // wait here covers that gap. When the drain already happened above, the
        // call returns immediately.
        await _process.WaitForExitWithinTimeoutAsync().ConfigureAwait(false);

        // Terminate the server process (or confirm it already exited), then build
        // and publish strongly-typed completion details while the process handle
        // is still valid so we can read the exit code.
        try
        {
            _process.Dispose(
                processRunning: true,
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

    private StdioClientCompletionDetails BuildCompletionDetails(Exception? error) =>
        _process.BuildCompletionDetails(error);
}
