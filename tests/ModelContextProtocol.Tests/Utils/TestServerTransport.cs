using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;

namespace ModelContextProtocol.Tests.Utils;

public class TestServerTransport : InMemoryServerTransport
{
    public List<IJsonRpcMessage> SentMessages { get; } = [];

    public override Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);

        return base.SendMessageAsync(message, cancellationToken);
    }

    protected override object? CreateMessageResult(JsonRpcRequest request)
    {
        if (request.Method == "roots/list")
        {
            return new ModelContextProtocol.Protocol.Types.ListRootsResult
            {
                Roots = []
            };
        }

        if (request.Method == "sampling/createMessage")
            return new CreateMessageResult { Content = new(), Model = "model", Role = "role" };

        return base.CreateMessageResult(request);
    }
}
