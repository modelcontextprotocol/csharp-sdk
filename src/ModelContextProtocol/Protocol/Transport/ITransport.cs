using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Represents a transport mechanism for MCP (Model Context Protocol) communication between clients and servers.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ITransport"/> interface is the core abstraction for bidirectional communication in the MCP architecture.
/// It provides methods for sending and receiving JSON-RPC messages regardless of the underlying transport mechanism,
/// allowing protocol implementation to be decoupled from communication details.
/// </para>
/// <para>
/// Implementations of <see cref="ITransport"/> handle the serialization, transmission, and reception of
/// MCP messages over various channels like HTTP (Server-Sent Events), standard input/output streams,
/// or arbitrary stream pairs.
/// </para>
/// <para>
/// While <see cref="IClientTransport"/> is responsible for establishing connections,
/// <see cref="ITransport"/> represents an established session. Client implementations typically obtain an
/// <see cref="ITransport"/> instance by calling <see cref="IClientTransport.ConnectAsync"/>.
/// </para>
/// <para>
/// The lifecycle of a transport typically follows this pattern:
/// <list type="number">
///   <item>Creation of the transport instance (via client connection or server acceptance)</item>
///   <item>Usage through SendMessageAsync/MessageReader while IsConnected is true</item>
///   <item>Disposal through DisposeAsync, which should clean up all resources</item>
/// </list>
/// </para>
/// <para>
/// Implementations must handle connection state changes, message serialization/deserialization,
/// and proper resource cleanup when disposed.
/// </para>
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the transport is currently connected and able to send/receive messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IsConnected"/> property indicates the current state of the transport connection.
    /// When <c>true</c>, the transport is ready to send and receive messages. When <c>false</c>,
    /// any attempt to send messages will typically throw an exception.
    /// </para>
    /// <para>
    /// The property transitions to <c>true</c> when the transport successfully establishes a connection,
    /// and transitions to <c>false</c> when the transport is disposed or encounters a connection error.
    /// </para>
    /// <para>
    /// Consumers should check this property before attempting to send messages to prevent exceptions.
    /// </para>
    /// </remarks>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a channel reader for receiving messages from the transport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="MessageReader"/> provides access to incoming JSON-RPC messages received by the transport.
    /// It returns a <see cref="ChannelReader{T}"/> which allows consuming messages in a thread-safe manner.
    /// </para>
    /// <para>
    /// This property is typically used in one of the following ways:
    /// <list type="bullet">
    ///   <item>
    ///     Async enumeration: <c>await foreach (var message in transport.MessageReader.ReadAllAsync(cancellationToken))</c>
    ///   </item>
    ///   <item>
    ///     Wait and read pattern: <c>await transport.MessageReader.WaitToReadAsync(cancellationToken); transport.MessageReader.TryRead(out var message);</c>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// The reader will continue to provide messages as long as the transport is connected. When the transport
    /// is disconnected or disposed, the channel will be completed and no more messages will be available.
    /// </para>
    /// <para>
    /// When using <see cref="MessageReader"/>, consumers should check <see cref="IsConnected"/> before performing long-running
    /// operations to ensure the transport remains viable throughout message processing.
    /// </para>
    /// </remarks>
    ChannelReader<IJsonRpcMessage> MessageReader { get; }

    /// <summary>
    /// Sends a JSON-RPC message through the transport.
    /// </summary>
    /// <param name="message">The JSON-RPC message to send. This can be any type that implements IJsonRpcMessage.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the transport is not connected.</exception>
    /// <remarks>
    /// <para>
    /// This method serializes and sends the provided JSON-RPC message through the transport connection.
    /// Before sending, it checks if the transport is connected using the <see cref="IsConnected"/> property.
    /// </para>
    /// <para>
    /// The behavior of this method varies by transport implementation:
    /// <list type="bullet">
    ///   <item>For SSE transports, messages are written to the SSE event stream</item>
    ///   <item>For stream transports, messages are serialized and written to the underlying stream</item>
    ///   <item>For stdio transports, messages are written to the standard output stream</item>
    /// </list>
    /// </para>
    /// <para>
    /// This is a core method used by higher-level abstractions in the MCP protocol implementation.
    /// Most client code should use the higher-level methods provided by <see cref="IMcpEndpoint"/>
    /// rather than accessing this method directly.
    /// </para>
    /// </remarks>
    Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default);
}
