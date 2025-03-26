using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Server;
using ModelContextProtocol.Utils;

using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Factory that creates linked in-memory client and server transports for testing purposes.
/// </summary>
public sealed class InMemoryTransport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTransport"/> class.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>

    public InMemoryTransport(McpServerOptions serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions), loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTransport"/> class.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>

    public InMemoryTransport(IOptions<McpServerOptions> serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions.Value), loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryTransport"/> class.
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
    public InMemoryTransport(string serverName, ILoggerFactory? loggerFactory = null)
    {
        var (clientTransport, serverTransport) = Create(serverName, loggerFactory);
        ServerTransport = serverTransport;
        ClientTransport = clientTransport;
    }

    /// <summary>
    /// Gets the client transport.
    /// </summary>
    public IClientTransport ClientTransport { get; }

    /// <summary>
    /// Gets the server transport.
    /// </summary>
    public IServerTransport ServerTransport { get; }


    private static (InMemoryClientTransport ClientTransport, InMemoryServerTransport ServerTransport) Create(
        string serverName,
        ILoggerFactory? loggerFactory = null)
    {
        // Configure client-to-server channel - this will be used for:
        // 1. Client's outgoing channel
        // 2. Server's MessageReader
        var clientToServerChannel = Channel.CreateUnbounded<IJsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,  // Both server and the server's MessageReader will read
            SingleWriter = true,   // Client writes
            AllowSynchronousContinuations = true
        });

        // Configure server-to-client channel - this will be used for:
        // 1. Server's outgoing channel
        // 2. Client's MessageReader
        var serverToClientChannel = Channel.CreateUnbounded<IJsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = false,  // Both client and the client's MessageReader will read
            SingleWriter = true,   // Server writes
            AllowSynchronousContinuations = true
        });

        // Create the client and server transports - they directly expose the channels through MessageReader
        var serverTransport = new InMemoryServerTransport(
            serverName,
            loggerFactory,
            clientToServerChannel.Reader,   // incoming: reads messages from client
            serverToClientChannel.Writer);   // outgoing: writes messages to client

        var clientTransport = new InMemoryClientTransport(
            serverName,
            loggerFactory,
            clientToServerChannel.Writer,   // outgoing: writes messages to server
            serverToClientChannel.Reader);   // incoming: reads messages from server

        // Link the transports together
        clientTransport.ServerTransport = serverTransport;

        return (clientTransport, serverTransport);
    }

    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);
        Throw.IfNull(serverOptions.ServerInfo);

        return serverOptions.ServerInfo.Name;
    }
}