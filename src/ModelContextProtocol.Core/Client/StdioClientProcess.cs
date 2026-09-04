using System.Diagnostics;

namespace ModelContextProtocol.Client;

/// <summary>Owns a child process started for a stdio client transport.</summary>
internal sealed class StdioClientProcess
{
    private readonly Queue<string> _stderrRollingLog;
    private readonly DataReceivedEventHandler _errorHandler;
    private int _disposed;

    internal StdioClientProcess(
        StdioClientTransportOptions options,
        Process process,
        Queue<string> stderrRollingLog,
        DataReceivedEventHandler errorHandler)
    {
        Options = options;
        Process = process;
        _stderrRollingLog = stderrRollingLog;
        _errorHandler = errorHandler;

        try
        {
            ProcessId = process.Id;
        }
        catch
        {
        }
    }

    internal StdioClientTransportOptions Options { get; }

    internal Process Process { get; }

    internal int? ProcessId { get; }

    internal Stream StandardInput => Process.StandardInput.BaseStream;

    internal Stream StandardOutput => Process.StandardOutput.BaseStream;

    internal bool HasExited => StdioClientTransport.HasExited(Process);

#if NET
    internal Task WaitForExitAsync(CancellationToken cancellationToken) =>
        Process.WaitForExitAsync(cancellationToken);
#endif

    internal async ValueTask WaitForExitWithinTimeoutAsync()
    {
        try
        {
#if NET
            using var timeoutCts = new CancellationTokenSource(Options.ShutdownTimeout);
            await Process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
#else
            if (Process.WaitForExit((int)Options.ShutdownTimeout.TotalMilliseconds))
            {
                Process.WaitForExit();
            }
#endif
        }
        catch
        {
        }
    }

    internal async ValueTask<Exception?> GetUnexpectedExitExceptionAsync()
    {
        if (!HasExited)
        {
            return null;
        }

        await WaitForExitWithinTimeoutAsync().ConfigureAwait(false);

        string? exitCode = null;
        try
        {
            exitCode = $" (exit code: {(uint)Process.ExitCode})";
        }
        catch
        {
        }

        string errorMessage = $"MCP server process exited unexpectedly{exitCode}";
        lock (_stderrRollingLog)
        {
            if (_stderrRollingLog.Count > 0)
            {
                errorMessage =
                    $"{errorMessage}{Environment.NewLine}" +
                    $"Server's stderr tail:{Environment.NewLine}" +
                    $"{string.Join(Environment.NewLine, _stderrRollingLog)}";
            }
        }

        return new IOException(errorMessage);
    }

    internal StdioClientCompletionDetails BuildCompletionDetails(Exception? error)
    {
        StdioClientCompletionDetails details = new()
        {
            Exception = error,
            ProcessId = ProcessId,
        };

        try
        {
            if (HasExited)
            {
                details.ExitCode = Process.ExitCode;
            }
        }
        catch
        {
        }

        lock (_stderrRollingLog)
        {
            if (_stderrRollingLog.Count > 0)
            {
                details.StandardErrorTail = _stderrRollingLog.ToArray();
            }
        }

        return details;
    }

    internal void CloseStandardInput() => Process.StandardInput.Close();

    internal void Dispose(bool processRunning, Action? beforeDispose = null)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Process.ErrorDataReceived -= _errorHandler;
        StdioClientTransport.DisposeProcess(Process, processRunning, Options.ShutdownTimeout, beforeDispose);
    }
}
