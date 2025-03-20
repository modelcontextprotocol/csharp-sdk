using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : IServerTransport
{
    private readonly Channel<IJsonRpcMessage> _messageChannel;
    private bool _isStarted;

    public bool IsConnected => _isStarted;

    public ChannelReader<IJsonRpcMessage> MessageReader => _messageChannel;

    public List<IJsonRpcMessage> SentMessages { get; } = [];

    public Action<IJsonRpcMessage>? OnMessageSent { get; set; }

    public TestServerTransport()
    {
        _messageChannel = Channel.CreateUnbounded<IJsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        if (message is JsonRpcRequest request)
        {
            if (request.Method == "roots/list")
                await ListRoots(request, cancellationToken);
            else if (request.Method == "sampling/createMessage")
                await Sampling(request, cancellationToken);
            else
                await WriteMessageAsync(request, cancellationToken);
        }
        else if (message is JsonRpcNotification notification)
        {
            await WriteMessageAsync(notification, cancellationToken);
        }

        OnMessageSent?.Invoke(message);
    }

    public Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        _isStarted = true;
        return Task.CompletedTask;
    }

    private async Task ListRoots(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = new ModelContextProtocol.Protocol.Types.ListRootsResult
            {
                Roots = []
            }
        }, cancellationToken);
    }

    private async Task Sampling(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = new CreateMessageResult { Content = new(), Model = "model", Role = "role" }
        }, cancellationToken);
    }

    private async Task Error(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcError
        {
            Id = request.Id,
            Error = new JsonRpcErrorDetail() { Code = -32601, Message = $"Method '{request.Method}' not supported" }
        }, cancellationToken);
    }

    protected async Task WriteMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }
}
