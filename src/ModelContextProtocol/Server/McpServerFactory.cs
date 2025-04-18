#if NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http;
#endif
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a factory for creating <see cref="IMcpServer"/> instances.
/// </summary>
/// <remarks>
/// This is the recommended way to create <see cref="IMcpServer"/> instances.
/// The factory handles proper initialization of server instances with the required dependencies.
/// </remarks>
public static class McpServerFactory
{
    /// <summary>
    /// Creates a new instance of an <see cref="IMcpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="contextRequest">Httprequest of the caller context</param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="IMcpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static IMcpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        HttpRequest contextRequest,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
    {
        Throw.IfNull(transport);
        Throw.IfNull(serverOptions);
        Throw.IfNull(contextRequest);

        return new McpServer(transport, serverOptions, contextRequest, loggerFactory, serviceProvider);
    }
}
