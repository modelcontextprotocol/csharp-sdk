using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a factory for creating <see cref="IMcpServer"/> instances.
/// </summary>
/// <remarks>
/// This is the recommended way to create <see cref="IMcpServer"/> instances.
/// The factory handles proper initialization of server instances with the required dependencies.
/// </remarks>
[Obsolete($"Use {nameof(McpServer)}.{nameof(McpServer.Create)} instead.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
public static class McpServerFactory
{
    /// <summary>
    /// Creates a new instance of an <see cref="IMcpServer"/>.
    /// </summary>
    /// <param name="transport">Transport to use for the server representing an already-established MCP session.</param>
    /// <param name="serverOptions">Configuration options for this server, including capabilities. </param>
    /// <param name="loggerFactory">Logger factory to use for logging. If null, logging will be disabled.</param>
    /// <param name="serviceProvider">Optional service provider to create new instances of tools and other dependencies.</param>
    /// <returns>An <see cref="IMcpServer"/> instance that should be disposed when no longer needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/>.</exception>
    public static IMcpServer Create(
        ITransport transport,
        McpServerOptions serverOptions,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? serviceProvider = null)
        => McpServer.Create(transport, serverOptions, loggerFactory, serviceProvider);
}
