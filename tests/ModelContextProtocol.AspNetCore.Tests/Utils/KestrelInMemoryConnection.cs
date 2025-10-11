using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using System.IO.Pipelines;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

public sealed class KestrelInMemoryConnection : ConnectionContext
{
    private readonly CancellationTokenSource _connectionClosedCts = new();
    private readonly FeatureCollection _features = new();

    public KestrelInMemoryConnection()
    {
        Pipe clientToServerPipe = new();
        Pipe serverToClientPipe = new();

        ConnectionClosed = _connectionClosedCts.Token;
        Transport = new DuplexPipe
        {
            Input = clientToServerPipe.Reader,
            Output = serverToClientPipe.Writer,
        };
        ClientPipe = new DuplexPipe
        {
            Input = serverToClientPipe.Reader,
            Output = clientToServerPipe.Writer,
        };
        ClientStream = new DuplexStream(ClientPipe, _connectionClosedCts);
    }

    public IDuplexPipe ClientPipe { get; }
    public Stream ClientStream { get; }

    public override IDuplexPipe Transport { get; set; }
    public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");

    public override IFeatureCollection Features => _features;

    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

    public override async ValueTask DisposeAsync()
    {
        // This is called by Kestrel. The client should dispose the DuplexStream which
        // completes the other half of these pipes.
        await Transport.Input.CompleteAsync();
        await Transport.Output.CompleteAsync();

        // Don't bother disposing the _connectionClosedCts, since this is just for testing,
        // and it's annoying to synchronize with DuplexStream.

        await base.DisposeAsync();
    }

    private class DuplexPipe : IDuplexPipe
    {
        public required PipeReader Input { get; init; }
        public required PipeWriter Output { get; init; }
    }

    private class DuplexStream(IDuplexPipe duplexPipe, CancellationTokenSource connectionClosedCts) : Stream
    {
        private readonly Stream _readStream = duplexPipe.Input.AsStream();
        private readonly Stream _writeStream = duplexPipe.Output.AsStream();

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Normally, Kestrel will trigger RequestAborted when the connectionClosedCts fires causing it to gracefully close
            // the connection. However, there's currently a race condition that can cause this to get missed. This at least
            // unblocks HttpConnection.SendAsync when it disposes the underlying connection stream while awaiting the _readAheadTask
            // as would happen with a real socket. https://github.com/dotnet/aspnetcore/pull/62385
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionClosedCts.Token);
            return await _readStream.ReadAsync(buffer, offset, count, linkedTokenSource.Token);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionClosedCts.Token);
            return await _readStream.ReadAsync(buffer, linkedTokenSource.Token);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _writeStream.WriteAsync(buffer, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken)
            => _writeStream.FlushAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            // Signal to the server the the client has closed the connection, and dispose the client-half of the Pipes.
            ThreadPool.UnsafeQueueUserWorkItem(static cts => ((CancellationTokenSource)cts!).Cancel(), connectionClosedCts);
            duplexPipe.Input.Complete();
            duplexPipe.Output.Complete();
        }

        // Unsupported stuff
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        // Don't bother supporting sync or APM methods. SocketsHttpHandler shouldn't use them.
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
