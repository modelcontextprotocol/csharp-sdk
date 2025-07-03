using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : ITransport
{
    private readonly Channel<JsonRpcMessage> _messageChannel;

    public bool IsConnected { get; set; }

    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel;

    public IList<JsonRpcMessage> SentMessages { get; } = [];

    public Action<JsonRpcMessage>? OnMessageSent { get; set; }

    public string? SessionId => null;

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
                await ListRootsAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.SamplingCreateMessage)
                await SamplingAsync(request, cancellationToken);
            else if (request.Method == RequestMethods.ElicitationCreate)
                await ElicitAsync(request, cancellationToken);
            else
                await WriteMessageAsync(request, cancellationToken);
        }
        else if (message is JsonRpcNotification notification)
        {
            await WriteMessageAsync(notification, cancellationToken);
        }

        OnMessageSent?.Invoke(message);
    }

    private async Task ListRootsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
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

    private async Task SamplingAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new CreateMessageResult { Content = new TextContentBlock { Text = "" }, Model = "model", Role = Role.User }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task ElicitAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        await WriteMessageAsync(new JsonRpcResponse
        {
            Id = request.Id,
            Result = JsonSerializer.SerializeToNode(new ElicitResult { Action = "decline" }, McpJsonUtilities.DefaultOptions),
        }, cancellationToken);
    }

    private async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
    }
}
