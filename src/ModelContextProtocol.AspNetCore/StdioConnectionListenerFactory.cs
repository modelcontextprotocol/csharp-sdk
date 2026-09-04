using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using System.IO.Pipelines;
using System.Net;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StdioConnectionListenerFactory : IConnectionListenerFactory, IConnectionListenerFactorySelector
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly Stream _input;
    private readonly Stream _output;

    public StdioConnectionListenerFactory(
        IHostApplicationLifetime applicationLifetime,
        HttpOverStdioStreams streams)
        : this(applicationLifetime, streams.Input, streams.Output)
    {
    }

    internal StdioConnectionListenerFactory(
        IHostApplicationLifetime applicationLifetime,
        Stream input,
        Stream output)
    {
        _applicationLifetime = applicationLifetime;
        _input = input;
        _output = output;
    }

    public bool CanBind(EndPoint endpoint) => ReferenceEquals(endpoint, StdioEndPoint.Instance);

    public ValueTask<IConnectionListener> BindAsync(
        EndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!CanBind(endpoint))
        {
            throw new NotSupportedException($"The HTTP-over-stdio transport cannot bind to endpoint '{endpoint}'.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<IConnectionListener>(cancellationToken);
        }

        return new(new StdioConnectionListener(
            endpoint,
            new StdioConnectionContext(_input, _output, _applicationLifetime)));
    }

    private sealed class StdioConnectionListener(
        EndPoint endpoint,
        StdioConnectionContext connection) : IConnectionListener
    {
        private readonly TaskCompletionSource _unbindCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();
        private bool _accepted;
        private bool _unbound;

        public EndPoint EndPoint => endpoint;

        public ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<ConnectionContext?>(cancellationToken);
            }

            lock (_sync)
            {
                if (_unbound)
                {
                    return new((ConnectionContext?)null);
                }

                if (!_accepted)
                {
                    _accepted = true;
                    return new(connection);
                }
            }

            return new(WaitForUnbindAsync(cancellationToken));
        }

        public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                _unbound = true;
            }

            _unbindCompletion.TrySetResult();
            return default;
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                _unbound = true;
            }

            _unbindCompletion.TrySetResult();
            return default;
        }

        private async Task<ConnectionContext?> WaitForUnbindAsync(CancellationToken cancellationToken)
        {
            await _unbindCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
    }
}

internal sealed class StdioConnectionContext : ConnectionContext
{
    private readonly CancellationTokenSource _connectionClosedSource = new();
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly FeatureCollection _features = new();
    private int _disposed;
    private int _applicationStopRequested;

    public StdioConnectionContext(
        Stream input,
        Stream output,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        _applicationLifetime = applicationLifetime;
        ConnectionClosed = _connectionClosedSource.Token;
        Transport = new StdioDuplexPipe(
            PipeReader.Create(
                new StopApplicationOnEofStream(input, RequestApplicationStop),
                new StreamPipeReaderOptions(leaveOpen: true)),
            PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true)));
    }

    public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");

    public override IFeatureCollection Features => _features;

    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

    public override IDuplexPipe Transport { get; set; }

    public override EndPoint? LocalEndPoint { get; set; } = StdioEndPoint.Instance;

    public override EndPoint? RemoteEndPoint { get; set; } = StdioEndPoint.Instance;

    public override CancellationToken ConnectionClosed { get; set; }

    public override void Abort(ConnectionAbortedException abortReason)
    {
        ArgumentNullException.ThrowIfNull(abortReason);

        _connectionClosedSource.Cancel();
        Transport.Input.CancelPendingRead();
        Transport.Output.CancelPendingFlush();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _connectionClosedSource.Cancel();
        try
        {
            await Transport.Input.CompleteAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Transport.Output.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                RequestApplicationStop();
            }
        }
    }

    private void RequestApplicationStop()
    {
        if (Interlocked.Exchange(ref _applicationStopRequested, 1) == 0)
        {
            _applicationLifetime.StopApplication();
        }
    }

    private sealed class StdioDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }

    private sealed class StopApplicationOnEofStream(
        Stream inner,
        Action stopApplication) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = inner.Read(buffer, offset, count);
            StopApplicationOnEof(bytesRead);
            return bytesRead;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int bytesRead = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            StopApplicationOnEof(bytesRead);
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int bytesRead = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            StopApplicationOnEof(bytesRead);
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            inner.Write(buffer, offset, count);

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
        }

        private void StopApplicationOnEof(int bytesRead)
        {
            if (bytesRead == 0)
            {
                stopApplication();
            }
        }
    }
}
