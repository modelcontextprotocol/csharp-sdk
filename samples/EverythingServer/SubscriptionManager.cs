using ModelContextProtocol;
using ModelContextProtocol.Server;

// This class manages subscriptions to resources by McpServer instances.
// The subscription information must be accessed in a thread-safe manner since handlers
// can run in parallel even in the context of a single session.
static class SubscriptionManager
{
    // Subscriptions tracks resource URIs to bags of McpServer instances (thread-safe via locking)
    private static Dictionary<string, List<IMcpServer>> subscriptions = new();

    // SessionSubscriptions is a secondary index to subscriptions to allow efficient removal of all
    // subscriptions for a given session when it ends. (thread-safe via locking)
    private static Dictionary<string /* sessionId */, List<string> /* uris */> sessionSubscriptions = new();

    private static readonly object _subscriptionsLock = new();

    public static void AddSubscription(string uri, IMcpServer server)
    {
        if (server.SessionId == null)
        {
            throw new McpException("Cannot add subscription for server with null SessionId");
        }
        lock (_subscriptionsLock)
        {
            subscriptions[uri] ??= new List<IMcpServer>();
            subscriptions[uri].Add(server);
            sessionSubscriptions[server.SessionId] ??= new List<string>();
            sessionSubscriptions[server.SessionId].Add(uri);
        }
    }

    public static void RemoveSubscription(string uri, IMcpServer server)
    {
        if (server.SessionId == null)
        {
            throw new McpException("Cannot remove subscription for server with null SessionId");
        }
        lock (_subscriptionsLock)
        {
            if (subscriptions.ContainsKey(uri))
            {
                // Remove the server from the list of subscriptions for the URI
                subscriptions[uri] = subscriptions[uri].Where(s => s.SessionId != server.SessionId).ToList();
                if (subscriptions[uri]?.Count == 0)
                {
                    subscriptions.Remove(uri);
                }
            }
            // Remove the URI from the list of subscriptions for the session
            sessionSubscriptions[server.SessionId]?.Remove(uri);
            if (sessionSubscriptions[server.SessionId]?.Count == 0)
            {
                sessionSubscriptions.Remove(server.SessionId);
            }
        }
    }

    public static IDictionary<string, List<IMcpServer>> GetSubscriptions()
    {
        lock (_subscriptionsLock)
        {
            // Return a copy of the subscriptions dictionary to avoid external modification
            return subscriptions.ToDictionary(entry => entry.Key,
                                             entry => entry.Value.ToList());
        }
    }

    public static void RemoveAllSubscriptions(IMcpServer server)
    {
        if (server.SessionId is { } sessionId)
        {
            lock (_subscriptionsLock)
            {
                // Remove all subscriptions for the session
                if (sessionSubscriptions.TryGetValue(sessionId, out var uris))
                {
                    foreach (var uri in uris)
                    {
                        subscriptions[uri] = subscriptions[uri].Where(s => s.SessionId != sessionId).ToList();
                        if (subscriptions[uri]?.Count == 0)
                        {
                            subscriptions.Remove(uri);
                        }
                    }
                    sessionSubscriptions.Remove(sessionId);
                }
            }
        }
    }
}