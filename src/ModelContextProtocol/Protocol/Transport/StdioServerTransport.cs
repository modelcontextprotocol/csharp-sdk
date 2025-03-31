using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a server MCP transport implemented via "stdio" (standard input/output).
/// </summary>
public sealed class StdioServerTransport : StreamServerTransport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(IOptions<McpServerOptions> serverOptions, ILoggerFactory? loggerFactory = null)
        : this(serverOptions?.Value!, loggerFactory: loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(McpServerOptions serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions), loggerFactory: loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class, using
    /// <see cref="Console.In"/> and <see cref="Console.Out"/> for input and output streams.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// By default, no logging is performed. If a <paramref name="loggerFactory"/> is supplied, it must not log
    /// to <see cref="Console.Out"/>, as that will interfere with the transport's output.
    /// </para>
    /// </remarks>
    public StdioServerTransport(string serverName, ILoggerFactory? loggerFactory = null)
        : base(Console.OpenStandardInput(),
               new BufferedStream(Console.OpenStandardOutput()),
               serverName ?? throw new ArgumentNullException(nameof(serverName)),
               loggerFactory)
    {
    }

    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);
        Throw.IfNull(serverOptions.ServerInfo);
        Throw.IfNull(serverOptions.ServerInfo.Name);

        return serverOptions.ServerInfo.Name;
    }
}
