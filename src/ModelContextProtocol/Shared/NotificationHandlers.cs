using ModelContextProtocol.Protocol.Messages;

namespace ModelContextProtocol.Shared;

internal sealed class NotificationHandlers : Dictionary<string, List<Func<JsonRpcNotification, Task>>>
{
    /// <summary>Adds a notification handler as part of configuring the endpoint.</summary>
    /// <remarks>This method is not thread-safe and should only be used serially as part of configuring the instance.</remarks>
    public void Add(string method, Func<JsonRpcNotification, Task> handler)
    {
        if (!TryGetValue(method, out var handlers))
        {
            this[method] = handlers = [];
        }

        handlers.Add(handler);
    }

    /// <summary>Adds notification handlers as part of configuring the endpoint.</summary>
    /// <remarks>This method is not thread-safe and should only be used serially as part of configuring the instance.</remarks>
    public void AddRange(IEnumerable<KeyValuePair<string, Func<JsonRpcNotification, Task>>> handlers)
    {
        foreach (var handler in handlers)
        {
            Add(handler.Key, handler.Value);
        }
    }
}
