using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that custom request handlers registered through
/// <see cref="McpServerOptions.RequestHandlers"/> cannot silently replace a built-in method or another custom handler.
/// </summary>
public class CustomRequestHandlerCollisionTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
#pragma warning disable MCPEXP002
    private static McpServerRequestHandler CreateHandler(string method, string? routingNameParameter = null) => new()
    {
        Method = method,
        RoutingNameParameter = routingNameParameter,
        Handler = (request, cancellationToken) => new ValueTask<JsonNode?>((JsonNode?)null),
    };

    [Theory]
    [InlineData("tools/call")]
    [InlineData("initialize")]
    public async Task CustomHandler_CollidingWithBuiltInMethod_Throws(string method)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            Capabilities = new() { Tools = new() },
            RequestHandlers = [CreateHandler(method)],
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(method, ex.Message);
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

    [Fact]
    public async Task CustomHandler_NullHandler_Throws()
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = "custom/method",
                    Handler = null!,
                },
            ],
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("null Handler", exception.Message);
        Assert.Contains("custom/method", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CustomHandler_EmptyRoutingNameParameter_Throws(string routingNameParameter)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers = [CreateHandler("custom/method", routingNameParameter)],
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpServerRequestHandler.RoutingNameParameter), ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CustomHandler_MissingMethod_Throws(string? method)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            RequestHandlers = [CreateHandler(method!)],
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));
        Assert.Contains("null or empty Method", exception.Message);
    }
#pragma warning restore MCPEXP002
}
