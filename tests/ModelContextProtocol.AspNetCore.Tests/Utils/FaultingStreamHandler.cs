using System.Diagnostics;
using System.Net;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

/// <summary>
/// A message handler that wraps SSE response streams and can trigger faults mid-stream
/// to simulate network disconnections during SSE streaming.
/// </summary>
internal sealed class FaultingStreamHandler : DelegatingHandler
{
    private FaultingStream? _lastStream;
    private TaskCompletionSource? _reconnectTcs;
    private TaskCompletionSource _unsolicitedMessageStreamReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForUnsolicitedMessageStreamAsync(CancellationToken cancellationToken = default)
        => _unsolicitedMessageStreamReadyTcs.Task.WaitAsync(cancellationToken);

    internal void SignalUnsolicitedMessageStreamReady() => _unsolicitedMessageStreamReadyTcs.TrySetResult();

    public async Task<ReconnectAttempt> TriggerFaultAsync(CancellationToken cancellationToken)
    {
        if (_lastStream is null or { IsDisposed: true })
        {
            throw new InvalidOperationException("There is no active response stream to fault.");
        }

        if (_reconnectTcs is not null)
        {
            throw new InvalidOperationException("Cannot trigger a fault while already waiting for reconnection.");
        }

        // Reset the TCS so we can wait for the reconnected unsolicited message stream
        _unsolicitedMessageStreamReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _reconnectTcs = new();
        await _lastStream.TriggerFaultAsync(cancellationToken);

        return new(_reconnectTcs);
    }

    public sealed class ReconnectAttempt(TaskCompletionSource reconnectTcs)
    {
        public void Continue()
            => reconnectTcs.SetResult();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_reconnectTcs is not null && request.Headers.Accept.Contains(new("text/event-stream")))
        {
            // If we're blocking reconnection, wait until we're allowed to continue.
            await _reconnectTcs.Task.WaitAsync(cancellationToken);
            _reconnectTcs = null;
        }

        var isGetRequest = request.Method == HttpMethod.Get;
        var response = await base.SendAsync(request, cancellationToken);

        // Only wrap SSE streams (text/event-stream)
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var originalStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            _lastStream = new FaultingStream(originalStream);
            var faultingContent = new FaultingStreamContent(_lastStream);

            // Copy headers from original content
            var newContent = faultingContent;
            foreach (var header in response.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = newContent;

            // For GET requests (unsolicited message stream), set up the stream to signal
            // when first data is read. This ensures the server's transport handler is ready.
            if (isGetRequest)
            {
                _lastStream.SetReadyCallback(SignalUnsolicitedMessageStreamReady);
            }
        }

        return response;
    }

    private sealed class FaultingStreamContent(FaultingStream stream) : HttpContent
    {
        private readonly FaultingStream _manualStream = new(stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class FaultingStream(Stream innerStream) : Stream
    {
        private readonly CancellationTokenSource _cts = new();
        private TaskCompletionSource? _faultTcs;
        private Action? _readyCallback;
        private bool _readySignaled;
        private bool _disposed;

        public bool IsDisposed => _disposed;

        public void SetReadyCallback(Action callback) => _readyCallback = callback;

        public async Task TriggerFaultAsync(CancellationToken cancellationToken)
        {
            if (_faultTcs is not null)
            {
                throw new InvalidOperationException("Only one fault can be triggered per stream.");
            }

            _faultTcs = new TaskCompletionSource();

            await _cts.CancelAsync();

            // Use a timeout to detect if the fault is not observed by a read operation.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                await _faultTcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"TriggerFaultAsync timed out after 30 seconds waiting for a read to observe the cancellation. " +
                    $"Stream disposed: {_disposed}, CTS cancelled: {_cts.IsCancellationRequested}");
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                _cts.Token.ThrowIfCancellationRequested();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
                var bytesRead = await innerStream.ReadAsync(buffer, linkedCts.Token);

                _cts.Token.ThrowIfCancellationRequested();

                if (bytesRead > 0 && !_readySignaled)
                {
                    _readySignaled = true;
                    _readyCallback?.Invoke();
                }

                return bytesRead;
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                Debug.Assert(_faultTcs is not null);

                if (!_faultTcs.TrySetResult())
                {
                    throw new InvalidOperationException("Attempted to read an already-faulted stream.");
                }

                throw new IOException("Simulated network disconnection.");
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Synchronous reads are not supported.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanWrite => innerStream.CanWrite;
        public override long Length => innerStream.Length;
        public override long Position { get => innerStream.Position; set => innerStream.Position = value; }
        public override void Flush() => innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
        public override void SetLength(long value) => innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => innerStream.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (!disposing || _disposed)
            {
                return;
            }

            _disposed = true;
            innerStream.Dispose();
        }
    }
}
