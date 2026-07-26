using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Transport;

public class StreamableHttpServerTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task SendMessageAsync_AfterGetRequestEnds_DoesNotWriteToResponseStream()
    {
        // Regression test for the SSE response stream being retained after the GET request
        // handler returns. Without releasing the stream reference, the Kestrel connection
        // and its associated memory pool buffers (~20MiB per SSE session) stay pinned in
        // unmanaged memory until the session is eventually disposed (via explicit DELETE or
        // idle timeout), causing steady memory growth for servers whose clients disconnect
        // without sending DELETE. After the GET handler returns, SendMessageAsync must not
        // attempt to write to the (now released) response stream.

        await using var transport = new StreamableHttpServerTransport()
        {
            SessionId = "test-session",
        };

        var responseStream = new RecordingStream();

        using var cts = new CancellationTokenSource();
        var getTask = transport.HandleGetRequestAsync(responseStream, cts.Token);

        // Wait until the GET handler has finished initialization (signaled by the initial
        // flush that sends HTTP response headers) so we know _httpSseWriter is set.
        await responseStream.FirstActivity.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        var writeCountBeforeCancel = responseStream.WriteCount;

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => getTask);

        await transport.SendMessageAsync(
            new JsonRpcNotification { Method = "test" },
            TestContext.Current.CancellationToken);

        Assert.Equal(writeCountBeforeCancel, responseStream.WriteCount);
    }

    private sealed class RecordingStream : Stream
    {
        private readonly TaskCompletionSource<bool> _firstActivity = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;

        public Task FirstActivity => _firstActivity.Task;
        public int WriteCount => Volatile.Read(ref _writeCount);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _firstActivity.TrySetResult(true);

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            _firstActivity.TrySetResult(true);
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref _writeCount);
            _firstActivity.TrySetResult(true);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _writeCount);
            _firstActivity.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
