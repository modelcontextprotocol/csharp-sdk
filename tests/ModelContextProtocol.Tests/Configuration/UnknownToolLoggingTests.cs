using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class UnknownToolLoggingTests : ClientServerTestBase
{
    public UnknownToolLoggingTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<TestTools>();
    }

    [Fact]
    public async Task Unknown_Tool_Returns_InvalidParams_Without_Logging_Tool_Failure()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () => await client.CallToolAsync(
            "no_such_tool",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("no_such_tool", exception.Message);
        Assert.DoesNotContain(MockLoggerProvider.LogMessages, message =>
            message.LogLevel == LogLevel.Error &&
            message.Message.Contains("no_such_tool") &&
            message.Message.Contains("threw an unhandled exception"));
    }

    [McpServerToolType]
    public sealed class TestTools
    {
        [McpServerTool]
        public static string Echo() => "ok";
    }
}
