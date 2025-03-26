using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol.Messages;

using System.Threading.Channels;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Factory that creates linked in-memory client and server transports for testing purposes.
/// </summary>
public sealed class InMemoryTransport
{
    /// <summary>
    /// Creates a new pair of in-memory transports for client and server communication.
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory for logging transport operations.</param>
    /// <returns>A tuple containing client and server transports that communicate with each other.</returns>
    public static (InMemoryClientTransport ClientTransport, InMemoryServerTransport ServerTransport) Create(
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
            loggerFactory,
            clientToServerChannel.Reader,   // incoming: reads messages from client
            serverToClientChannel.Writer);   // outgoing: writes messages to client

        var clientTransport = new InMemoryClientTransport(
            loggerFactory,
            clientToServerChannel.Writer,   // outgoing: writes messages to server
            serverToClientChannel.Reader);   // incoming: reads messages from server

        // Link the transports together
        clientTransport.ServerTransport = serverTransport;

        return (clientTransport, serverTransport);
    }
}