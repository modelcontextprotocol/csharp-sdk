using ModelContextProtocol.Protocol.Messages;
using System.Collections.Concurrent;

namespace ModelContextProtocol.Shared;

internal sealed class NotificationHandlers : ConcurrentDictionary<string, List<Func<JsonRpcNotification, Task>>>
{
    public void Add(string method, Func<JsonRpcNotification, Task> handler)
    {
        var handlers = GetOrAdd(method, _ => []);
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }
}
