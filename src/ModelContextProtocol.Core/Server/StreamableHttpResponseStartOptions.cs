using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

internal sealed class StreamableHttpResponseStartOptions(Action<JsonRpcMessage> onResponseStarting)
{
    public Action<JsonRpcMessage> OnResponseStarting { get; } = onResponseStarting;
}
