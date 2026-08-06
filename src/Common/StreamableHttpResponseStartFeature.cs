using ModelContextProtocol.Protocol;

namespace ModelContextProtocol;

internal static class StreamableHttpResponseStartFeature
{
    private const string ResponseStartingCallbackKey = "ModelContextProtocol.StreamableHttp.ResponseStartingCallback";

    public static void Set(JsonRpcMessage message, Action<JsonRpcMessage> callback)
    {
        message.Context ??= new();
        (message.Context.Items ??= new Dictionary<string, object?>())[ResponseStartingCallbackKey] = callback;
    }

    public static Action<JsonRpcMessage>? Take(JsonRpcMessage message)
    {
        if (message.Context?.Items is not { } items ||
            !items.TryGetValue(ResponseStartingCallbackKey, out var value))
        {
            return null;
        }

        items.Remove(ResponseStartingCallbackKey);
        return (Action<JsonRpcMessage>)value!;
    }
}
