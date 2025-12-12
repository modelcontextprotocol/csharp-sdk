using Microsoft.AspNetCore.Connections;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

public sealed class KestrelInMemoryTransport : IConnectionListenerFactory
{
    // Socket accept queues keyed by listen port.
    private readonly ConcurrentDictionary<int, Channel<ConnectionContext>> _acceptQueues = [];

    public KestrelInMemoryConnection CreateConnection(EndPoint endpoint)
    {
        if (!_acceptQueues.TryGetValue(GetEndpointPort(endpoint), out var acceptQueue))
        {
            throw new IOException($"No listener is bound to endpoint '{endpoint}'.");
        }

        var connection = new KestrelInMemoryConnection();
        if (!acceptQueue.Writer.TryWrite(connection))
        {
            throw new IOException("The KestrelInMemoryTransport has been shut down.");
        };

        return connection;
    }

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        var acceptQueue = _acceptQueues.GetOrAdd(GetEndpointPort(endpoint), _ => Channel.CreateUnbounded<ConnectionContext>());
        return new(new KestrelInMemoryListener(endpoint, acceptQueue));
    }

    private static int GetEndpointPort(EndPoint endpoint) =>
        endpoint switch
        {
            DnsEndPoint dnsEndpoint => dnsEndpoint.Port,
            IPEndPoint ipEndpoint => ipEndpoint.Port,
            _ => throw new InvalidOperationException($"Unexpected endpoint type: '{endpoint.GetType()}'"),
        };

    private sealed class KestrelInMemoryListener(EndPoint endpoint, Channel<ConnectionContext> acceptQueue) : IConnectionListener
    {
        public EndPoint EndPoint => endpoint;

        public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var item in acceptQueue.Reader.ReadAllAsync(cancellationToken))
            {
                return item;
            }

            return null;
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            acceptQueue.Writer.TryComplete();
            return default;
        }

        public ValueTask DisposeAsync() => UnbindAsync(CancellationToken.None);
    }
}
