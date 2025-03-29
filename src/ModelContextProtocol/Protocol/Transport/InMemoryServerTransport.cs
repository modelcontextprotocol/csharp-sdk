using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// InMemory server transport for special scenarios or testing. 
/// </summary>
public class InMemoryServerTransport : IServerTransport
{
    private readonly Channel<IJsonRpcMessage> _messageChannel;
    private bool _isStarted;

    /// <inheritdoc/>
    public bool IsConnected => _isStarted;

    /// <inheritdoc/>
    public ChannelReader<IJsonRpcMessage> MessageReader => _messageChannel;

    /// <summary>
    /// Delegate to handle messages before sending them.
    /// </summary>
    public Func<IJsonRpcMessage, CancellationToken, Task<IJsonRpcMessage?>>? HandleMessage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryServerTransport"/> class.
    /// </summary>
    public InMemoryServerTransport()
    {
        _messageChannel = Channel.CreateUnbounded<IJsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // default message handler
        HandleMessage = (m, _) => Task.FromResult(CreateResponseMessage(m));
    }

    /// <inheritdoc/>
#if NET8_0_OR_GREATER
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
#else
    public ValueTask DisposeAsync() => new ValueTask(Task.CompletedTask);
#endif

    /// <inheritdoc/>
    public virtual async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        IJsonRpcMessage? response = message;

        if (HandleMessage != null)
            response = await HandleMessage(message, cancellationToken);

        if (response != null)
            await WriteMessageAsync(response, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        _isStarted = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a message to the channel.
    /// </summary>
    protected virtual async Task WriteMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }

    /// <summary>
    /// Creates a response message for the given request.
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    protected virtual IJsonRpcMessage? CreateResponseMessage(IJsonRpcMessage message)
    {
        if (message is JsonRpcRequest request)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = CreateMessageResult(request)
            };
        }

        return message;
    }

    /// <summary>
    /// Creates a result object for the given request.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    protected virtual object? CreateMessageResult(JsonRpcRequest request)
    {
        return null;
    }
}
