using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that custom request handlers registered through
/// <see cref="McpServerOptions.RequestHandlers"/> cannot silently replace a built-in method
/// or another custom handler.
/// </summary>
public class CustomRequestHandlerCollisionTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
#pragma warning disable MCPEXP002
    private static McpServerRequestHandler CreateHandler(string method) => new()
    {
        Method = method,
        Handler = (request, cancellationToken) => new ValueTask<JsonNode?>((JsonNode?)null),
    };

    [Fact]
    public async Task CustomHandler_CollidingWithBuiltInMethod_Throws()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            Capabilities = new() { Tools = new() },
            RequestHandlers = [CreateHandler("tools/call")],
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("tools/call", ex.Message);
    }

    [Fact]
    public async Task CustomHandler_CollidingWithInitialize_Throws()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers = [CreateHandler("initialize")],
        };

        Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));
    }

    [Fact]
    public async Task CustomHandler_DuplicateCustomMethod_Throws()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers = [CreateHandler("custom/method"), CreateHandler("custom/method")],
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("custom/method", ex.Message);
    }

    [Fact]
    public async Task CustomHandler_UniqueMethod_Succeeds()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers = [CreateHandler("custom/method")],
        };

        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Assert.NotNull(server);
    }
#pragma warning restore MCPEXP002
}
