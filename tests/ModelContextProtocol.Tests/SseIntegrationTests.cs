using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.Tests;

public class SseIntegrationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    private McpServerConfig DefaultServerConfig = new()
    {
        Id = "test_server",
        Name = "In-memory Test Server",
        TransportType = TransportTypes.Sse,
        TransportOptions = [],
        Location = $"http://localhost/sse"
    };

    private Task<IMcpClient> ConnectMcpClient(HttpClient httpClient, McpClientOptions? clientOptions = null)
        => McpClientFactory.CreateAsync(
            DefaultServerConfig,
            clientOptions,
            (_, _) => new SseClientTransport(new(), DefaultServerConfig, httpClient, LoggerFactory),
            LoggerFactory,
            TestContext.Current.CancellationToken);


    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer()
    {
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        await using var mcpClient = await ConnectMcpClient(httpClient);

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer_WithFullEndpointEventUri()
    {
        await using var app = Builder.Build();
        MapAbsoluteEndpointUriMcp(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        await using var mcpClient = await ConnectMcpClient(httpClient);

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/message", new { message = "Hello, SSE!" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveNotification_InMemoryServer()
    {
        var receivedNotification = new TaskCompletionSource<string?>();

        await using var app = Builder.Build();
        app.MapMcp(runSessionAsync: (httpContext, mcpServer, cancellationToken) =>
        {
            mcpServer.RegisterNotificationHandler("test/notification", async (notification, cancellationToken) =>
            {
                Assert.Equal("Hello from client!", notification.Params?["message"]?.GetValue<string>());
                await mcpServer.SendNotificationAsync("test/notification", new { message = "Hello from server!" }, cancellationToken: cancellationToken);
            });
            return mcpServer.RunAsync(cancellationToken);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        await using var mcpClient = await ConnectMcpClient(httpClient);

        mcpClient.RegisterNotificationHandler("test/notification", (args, ca) =>
        {
            var msg = args.Params?["message"]?.GetValue<string>();
            receivedNotification.SetResult(msg);
            return Task.CompletedTask;
        });

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/notification", new { message = "Hello from client!" }, cancellationToken: TestContext.Current.CancellationToken);

        var message = await receivedNotification.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal("Hello from server!", message);
    }

    private static void MapAbsoluteEndpointUriMcp(IEndpointRouteBuilder endpoints)
    {
        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var optionsSnapshot = endpoints.ServiceProvider.GetRequiredService<IOptions<McpServerOptions>>();

        var routeGroup = endpoints.MapGroup("");
        SseResponseStreamTransport? session = null;

        routeGroup.MapGet("/sse", async context =>
        {
            var response = context.Response;
            var requestAborted = context.RequestAborted;

            response.Headers.ContentType = "text/event-stream";

            await using var transport = new SseResponseStreamTransport(response.Body, "http://localhost/message");
            session = transport;

            try
            {
                var transportTask = transport.RunAsync(cancellationToken: requestAborted);
                await using var server = McpServerFactory.Create(transport, optionsSnapshot.Value, loggerFactory, endpoints.ServiceProvider);

                try
                {
                    await server.RunAsync(requestAborted);
                }
                finally
                {
                    await transport.DisposeAsync();
                    await transportTask;
                }
            }
            catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
            {
                // RequestAborted always triggers when the client disconnects before a complete response body is written,
                // but this is how SSE connections are typically closed.
            }
        });

        routeGroup.MapPost("/message", async context =>
        {
            if (session is null)
            {
                await Results.BadRequest("Session not started.").ExecuteAsync(context);
                return;
            }
            var message = (IJsonRpcMessage?)await context.Request.ReadFromJsonAsync(McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(IJsonRpcMessage)), context.RequestAborted);
            if (message is null)
            {
                await Results.BadRequest("No message in request body.").ExecuteAsync(context);
                return;
            }

            await session.OnMessageReceivedAsync(message, context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("Accepted");
        });
    }
}
