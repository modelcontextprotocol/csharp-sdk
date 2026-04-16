using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Reflection;

namespace ModelContextProtocol.Tests.Transport;

public class StreamableHttpServerTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task HandleGetRequestAsync_ReleasesStreamReference_AfterRequestEnds()
    {
        await using var transport = new StreamableHttpServerTransport()
        {
            SessionId = "test-session",
        };

        var pipe = new Pipe();
        var responseStream = pipe.Writer.AsStream();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        try
        {
            await transport.HandleGetRequestAsync(responseStream, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // After the GET request handler returns, the transport must not retain a reference to the
        // response stream via _httpSseWriter. Otherwise the Kestrel connection and its associated
        // memory pool buffers (which can be ~20MiB per SSE session) stay pinned in unmanaged memory
        // until the session is eventually disposed (via explicit DELETE or idle timeout), causing
        // steady memory growth for servers whose clients disconnect without sending DELETE.
        var httpSseWriterField = typeof(StreamableHttpServerTransport).GetField(
            "_httpSseWriter",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(httpSseWriterField);
        var httpSseWriterValue = httpSseWriterField.GetValue(transport);
        Assert.Null(httpSseWriterValue);

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }
}
