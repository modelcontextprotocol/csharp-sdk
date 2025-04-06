using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests.Transport;

public class SseResponseStreamTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task Can_Customize_MessageEndpoint()
    {
        var responsePipe = new Pipe();

        await using var transport = new SseResponseStreamTransport(responsePipe.Writer.AsStream(), "/my-message-endpoint");
        var transportRunTask = transport.RunAsync(TestContext.Current.CancellationToken);

        using var responseStreamReader = new StreamReader(responsePipe.Reader.AsStream());
        var firstLine = await responseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("event: endpoint", firstLine);

        var secondLine = await responseStreamReader.ReadLineAsync(TestContext.Current.CancellationToken);
        Assert.Equal("data: /my-message-endpoint", secondLine);

        responsePipe.Reader.Complete();
        responsePipe.Writer.Complete();
    }
}
