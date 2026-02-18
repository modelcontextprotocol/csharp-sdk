using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

public class StreamClientTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Fact]
    public async Task SendMessageAsync_Should_Use_LF_Not_CRLF()
    {
        using var serverInput = new MemoryStream();
        Pipe serverOutputPipe = new();

        var transport = new StreamClientTransport(serverInput, serverOutputPipe.Reader.AsStream(), LoggerFactory);
        await using var sessionTransport = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };

        await sessionTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);

        byte[] bytes = serverInput.ToArray();

        // The output should end with exactly \n (0x0A), not \r\n (0x0D 0x0A).
        Assert.True(bytes.Length > 1, "Output should contain message data");
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\r', bytes[^2]);

        // Also verify the JSON content is valid
        var json = Encoding.UTF8.GetString(bytes).TrimEnd('\n');
        var expected = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
        Assert.Equal(expected, json);
    }

    [Fact]
    public async Task ReadMessagesAsync_Should_Accept_LF_Delimited_Messages()
    {
        Pipe serverInputPipe = new();
        Pipe serverOutputPipe = new();

        var transport = new StreamClientTransport(serverInputPipe.Writer.AsStream(), serverOutputPipe.Reader.AsStream(), LoggerFactory);
        await using var sessionTransport = await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest { Method = "test", Id = new RequestId(44) };
        var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);

        // Write a \n-delimited message to the server's output (which the client reads)
        await serverOutputPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"{json}\n"), TestContext.Current.CancellationToken);

        var canRead = await sessionTransport.MessageReader.WaitToReadAsync(TestContext.Current.CancellationToken);

        Assert.True(canRead, "Should be able to read a \\n-delimited message");
        Assert.True(sessionTransport.MessageReader.TryPeek(out var readMessage));
        Assert.NotNull(readMessage);
        Assert.IsType<JsonRpcRequest>(readMessage);
        Assert.Equal("44", ((JsonRpcRequest)readMessage).Id.ToString());
    }
}
