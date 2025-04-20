using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol.Messages;
using System;
using System.Diagnostics;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>Provides the client side of a stdio-based session transport.</summary>
internal sealed class StdioClientSessionTransport : StreamClientSessionTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly Process _process;

    public StdioClientSessionTransport(StdioClientTransportOptions options, Process process, string endpointName, ILoggerFactory? loggerFactory)
        : base(process.StandardInput, process.StandardOutput, endpointName, loggerFactory)
    {
        _process = process;
        _options = options;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// For stdio-based transports, this implementation first verifies that the underlying process 
    /// is still running before attempting to send the message. If the process has exited or cannot
    /// be accessed, a <see cref="InvalidOperationException"/> is thrown with details about the failure.
    /// </para>
    /// <para>
    /// After verifying the process state, this method delegates to the base class implementation
    /// to handle the actual message serialization and transmission to the process's standard input stream.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the underlying process has exited or cannot be accessed.
    /// </exception>
    public override async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Exception? processException = null;
        bool hasExited = false;
        try
        {
            hasExited = _process.HasExited;
        }
        catch (Exception e)
        {
            processException = e;
            hasExited = true;
        }

        if (hasExited)
        {
            throw new InvalidOperationException("Transport is not connected", processException);
        }

        await base.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override ValueTask CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            StdioClientTransport.DisposeProcess(_process, processRunning: true, _options.ShutdownTimeout, Name);
        }
        catch (Exception ex)
        {
            LogTransportShutdownFailed(Name, ex);
        }

        return base.CleanupAsync(cancellationToken);
    }
}
