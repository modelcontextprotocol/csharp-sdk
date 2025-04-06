﻿using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Utils;

public sealed class KestrelInMemoryConnection : ConnectionContext
{
    private readonly Pipe _clientToServerPipe = new();
    private readonly Pipe _serverToClientPipe = new();
    private readonly CancellationTokenSource _connectionClosedCts = new CancellationTokenSource();
    private readonly IFeatureCollection _features = new FeatureCollection();

    public KestrelInMemoryConnection()
    {
        ConnectionClosed = _connectionClosedCts.Token;
        Transport = new DuplexPipe
        {
            Input = _clientToServerPipe.Reader,
            Output = _serverToClientPipe.Writer,
        };
        Application = new DuplexPipe
        {
            Input = _serverToClientPipe.Reader,
            Output = _clientToServerPipe.Writer,
        };
        ClientStream = new DuplexStream(Application, _connectionClosedCts);
    }

    public IDuplexPipe Application { get; }
    public Stream ClientStream { get; }

    public override IDuplexPipe Transport { get; set; }
    public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");

    public override IFeatureCollection Features => _features;

    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

    public override ValueTask DisposeAsync()
    {
        // This is called by Kestrel. The client should dispose the DuplexStream which
        // completes the other half of these pipes.
        _serverToClientPipe.Writer.Complete();
        _serverToClientPipe.Reader.Complete();
        // Disposing the CTS without waiting for the client would be problematic
        // except we always dispose the HttpClient before Kestrel in our tests.
        _connectionClosedCts.Dispose();
        return base.DisposeAsync();
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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _readStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _readStream.ReadAsync(buffer, cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => _writeStream.WriteAsync(buffer, cancellationToken);

        public override Task FlushAsync(CancellationToken cancellationToken)
            => _writeStream.FlushAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            // Signal to the server the the client has closed the connection, and dispose the client-half of the Pipes.
            await connectionClosedCts.CancelAsync();
            duplexPipe.Input.Complete();
            duplexPipe.Output.Complete();
            await base.DisposeAsync();
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
