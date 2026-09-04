using HttpOverStdioServer;
using ModelContextProtocol.Tests.Utils;
using System.Buffers;
using System.IO.Pipelines;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StdioTransportDetectorTests
{
    private static readonly byte[] s_http2ConnectionPreface =
        "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

    [Fact]
    public async Task CompleteHttp2Preface_SelectsHttp2WithoutConsumingInput()
    {
        byte[] trailingBytes = [0, 1, 2, 3];
        byte[] input = [.. s_http2ConnectionPreface, .. trailingBytes];

        await AssertDetectionAsync(
            [input],
            completeWriter: false,
            StdioTransportKind.Http2,
            input);
    }

    [Fact]
    public async Task FragmentedHttp2Preface_SelectsHttp2AtEverySplit()
    {
        for (int split = 1; split < s_http2ConnectionPreface.Length; split++)
        {
            await AssertDetectionAsync(
                [
                    s_http2ConnectionPreface.AsMemory(0, split),
                    s_http2ConnectionPreface.AsMemory(split),
                ],
                completeWriter: false,
                StdioTransportKind.Http2,
                s_http2ConnectionPreface);
        }
    }

    [Fact]
    public async Task FirstDivergingByte_SelectsLegacyWithoutConsumingInput()
    {
        byte[] input = """{"jsonrpc":"2.0","method":"initialize"}"""u8.ToArray();

        await AssertDetectionAsync(
            [input],
            completeWriter: false,
            StdioTransportKind.Legacy,
            input);
    }

    [Fact]
    public async Task PartialPrefaceAtEndOfStream_SelectsLegacyWithoutConsumingInput()
    {
        byte[] input = s_http2ConnectionPreface[..10];

        await AssertDetectionAsync(
            [input],
            completeWriter: true,
            StdioTransportKind.Legacy,
            input);
    }

    private static async Task AssertDetectionAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> segments,
        bool completeWriter,
        StdioTransportKind expectedTransport,
        byte[] expectedInput)
    {
        var pipe = new Pipe();
        Task<StdioTransportKind> detectionTask =
            StdioTransportDetector.DetectAsync(
                pipe.Reader,
                TestContext.Current.CancellationToken).AsTask();

        for (int i = 0; i < segments.Count; i++)
        {
            await pipe.Writer.WriteAsync(segments[i], TestContext.Current.CancellationToken);

            if (i < segments.Count - 1)
            {
                Assert.False(detectionTask.IsCompleted);
            }
        }

        if (completeWriter)
        {
            await pipe.Writer.CompleteAsync();
        }

        Assert.Equal(
            expectedTransport,
            await detectionTask.WaitAsync(
                TestConstants.DefaultTimeout,
                TestContext.Current.CancellationToken));

        ReadResult readResult = await pipe.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(expectedInput, readResult.Buffer.ToArray());
        pipe.Reader.AdvanceTo(readResult.Buffer.End);

        await pipe.Reader.CompleteAsync();
        if (!completeWriter)
        {
            await pipe.Writer.CompleteAsync();
        }
    }
}
