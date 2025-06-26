using Microsoft.AspNetCore.Connections;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

public sealed class KestrelInMemoryTransport : IConnectionListenerFactory
{
    // socket accept queues keyed by listen port.
    private readonly ConcurrentDictionary<int, Channel<ConnectionContext>> _acceptQueues = [];

    public KestrelInMemoryConnection CreateConnection(EndPoint endpoint)
    {
        var connection = new KestrelInMemoryConnection();
        GetAcceptQueue(endpoint).Writer.TryWrite(connection);
        return connection;
    }

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default) =>
        new(new KestrelInMemoryListener(endpoint, GetAcceptQueue(endpoint)));

    private Channel<ConnectionContext> GetAcceptQueue(EndPoint endpoint) =>
        _acceptQueues.GetOrAdd(GetEndpointPort(endpoint), _ => Channel.CreateUnbounded<ConnectionContext>());

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
            if (await acceptQueue.Reader.WaitToReadAsync(cancellationToken))
            {
                while (acceptQueue.Reader.TryRead(out var item))
                {
                    return item;
                }
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
