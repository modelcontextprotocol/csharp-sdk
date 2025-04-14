using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : ITransport
{
    private readonly Channel<JsonRpcMessage> _messageChannel;

    public bool IsConnected { get; set; }

    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel;

    public List<JsonRpcMessage> SentMessages { get; } = [];

    public Action<JsonRpcMessage>? OnMessageSent { get; set; }

    public TestServerTransport()
    {
        _messageChannel = Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions
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
        return default;
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
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
            Result = JsonSerializer.SerializeToNode(new ListRootsResult
            {
                Roots = []
            }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task Sampling(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new CreateMessageResult { Content = new(), Model = "model", Role = Role.User }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }
}
