using Microsoft.AspNetCore.Connections;
using System.Net;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Utils;

public sealed class KestrelInMemoryTransport : IConnectionListenerFactory, IConnectionListener
{
    private readonly Channel<ConnectionContext> _acceptQueue = Channel.CreateUnbounded<ConnectionContext>();
    private EndPoint? _endPoint;

    public EndPoint EndPoint => _endPoint ?? throw new InvalidOperationException("EndPoint is not set. Call BindAsync first.");

    public KestrelInMemoryConnection CreateConnection()
    {
        var connection = new KestrelInMemoryConnection();
        _acceptQueue.Writer.TryWrite(connection);
        return connection;
    }

    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (await _acceptQueue.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_acceptQueue.Reader.TryRead(out var item))
            {
                return item;
            }
        }

        return null;
    }

    public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        _endPoint = endpoint;
        return new ValueTask<IConnectionListener>(this);
    }

    public ValueTask DisposeAsync()
    {
        return UnbindAsync(default);
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        _acceptQueue.Writer.TryComplete();
        return default;
    }
}
