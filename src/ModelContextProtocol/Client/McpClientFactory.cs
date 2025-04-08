using System.Globalization;
using System.Runtime.InteropServices;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelContextProtocol.Client;

/// <summary>Provides factory methods for creating MCP clients.</summary>
public static class McpClientFactory
{
    /// <summary>Creates an <see cref="IMcpClient"/>, connecting it to the specified server.</summary>
    /// <param name="clientTransport">The transport instance used to communicate with the server.</param>
    /// <param name="clientOptions">
    /// A client configuration object which specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process will be employed.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="IMcpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    public static async Task<IMcpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);

        string endpointName = clientTransport.EndpointName;
        var logger = loggerFactory?.CreateLogger(typeof(McpClientFactory)) ?? NullLogger.Instance;
        logger.CreatingClient(endpointName);

        McpClient client = new(clientTransport, clientOptions, loggerFactory);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            logger.ClientCreated(endpointName);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}