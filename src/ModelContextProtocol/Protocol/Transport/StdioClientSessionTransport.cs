using Microsoft.Extensions.Logging;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Messages;
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
    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_process.HasExited)
        {
            Logger.TransportNotConnected(EndpointName);
            throw new McpTransportException("Transport is not connected");
        }

        await base.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override ValueTask CleanupAsync(CancellationToken cancellationToken)
    {
        StdioClientTransport.DisposeProcess(_process, processStarted: true, Logger, _options.ShutdownTimeout, EndpointName);

        return base.CleanupAsync(cancellationToken);
    }
}
