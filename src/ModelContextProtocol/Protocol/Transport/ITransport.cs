using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for MCP communication.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the transport is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Channel for receiving messages from the transport.
    /// </summary>
    ChannelReader<IJsonRpcMessage> MessageReader { get; }

    /// <summary>
    /// Sends a message through the transport.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);
}
