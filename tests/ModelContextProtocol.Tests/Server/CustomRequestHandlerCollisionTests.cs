using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Verifies that custom request handlers registered through
/// <see cref="McpServerOptions.RequestHandlers"/> and <see cref="McpServerOptions.ContextualRequestHandlers"/>
/// cannot silently replace a built-in method or another custom handler.
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

    private static void AddHandler(McpServerOptions options, string method, bool contextual, string? routingNameParameter = null)
    {
        if (contextual)
        {
            options.ContextualRequestHandlers ??= [];
            options.ContextualRequestHandlers.Add(new McpServerContextualRequestHandler
            {
                Method = method,
                RoutingNameParameter = routingNameParameter,
                Handler = (request, cancellationToken) => new ValueTask<JsonNode?>((JsonNode?)null),
            });
        }
        else
        {
            options.RequestHandlers ??= [];
            options.RequestHandlers.Add(CreateHandler(method, routingNameParameter));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomHandler_CollidingWithBuiltInMethod_Throws(bool contextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions
        {
            Capabilities = new() { Tools = new() },
        };
        AddHandler(options, "tools/call", contextual);

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("tools/call", ex.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomHandler_CollidingWithInitialize_Throws(bool contextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        AddHandler(options, "initialize", contextual);

        Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CustomHandler_DuplicateCustomMethod_Throws(bool firstContextual, bool secondContextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        AddHandler(options, "custom/method", firstContextual);
        AddHandler(options, "custom/method", secondContextual);

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("custom/method", ex.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomHandler_UniqueMethod_Succeeds(bool contextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        AddHandler(options, "custom/method", contextual);

        await using var server = McpServer.Create(transport, options, LoggerFactory);
        Assert.NotNull(server);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomHandler_NullHandler_Throws(bool contextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        if (contextual)
        {
            options.ContextualRequestHandlers =
            [
                new McpServerContextualRequestHandler
                {
                    Method = "custom/method",
                    Handler = null!,
                },
            ];
        }
        else
        {
            options.RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = "custom/method",
                    Handler = null!,
                },
            ];
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains("null Handler", exception.Message);
        Assert.Contains("custom/method", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomHandler_EmptyRoutingNameParameter_Throws(bool contextual)
    {
        await using var transport = new StreamServerTransport(Stream.Null, Stream.Null);
        var options = new McpServerOptions();
        AddHandler(options, "custom/method", contextual, routingNameParameter: " ");

        var ex = Assert.Throws<InvalidOperationException>(
            () => McpServer.Create(transport, options, LoggerFactory));

        Assert.Contains(nameof(McpServerRequestHandler.RoutingNameParameter), ex.Message);
    }

    [Fact]
    public async Task LegacyHandler_RemainsCallableWithoutANullCheck()
    {
        var registration = CreateHandler("custom/method");

        var result = await registration.Handler(
            new JsonRpcRequest { Method = registration.Method }, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
#pragma warning restore MCPEXP002
}
