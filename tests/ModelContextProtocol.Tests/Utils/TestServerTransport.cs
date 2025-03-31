using System.Threading.Channels;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : ITransport
{
    private readonly Channel<IJsonRpcMessage> _messageChannel;

    public bool IsConnected { get; set; }

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
        IsConnected = true;
    }

    public ValueTask DisposeAsync()
    {
        _messageChannel.Writer.TryComplete();
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        if (message is JsonRpcRequest request)
        {
            if (request.Method == RequestMethods.RootsList)
                await ListRoots(request, cancellationToken);
            else if (request.Method == RequestMethods.SamplingCreateMessage)
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

    private async Task WriteMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }
}
