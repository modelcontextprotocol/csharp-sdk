﻿using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a factory for creating <see cref="IMcpServer"/> instances.
/// </summary>
public static class McpServerFactory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IMcpServer"/> class.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">
    /// Configuration options for this server, including capabilities. 
    /// Make sure to accurately reflect exactly what capabilities the server supports and does not support.
    /// </param>
    /// <param name="loggerFactory">Logger factory to use for logging</param>
    /// <param name="serviceProvider">Optional service provider to create new instances.</param>
    /// <returns>An <see cref="IMcpServer"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static IMcpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);

        return new McpServer(transport, serverOptions, loggerFactory, serviceProvider);
    }
}
