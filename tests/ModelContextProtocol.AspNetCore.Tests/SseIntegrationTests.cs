using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Serialization;
using TestServerWithHosting.Tools;

namespace ModelContextProtocol.AspNetCore.Tests;

public partial class SseIntegrationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    private readonly HttpClientTransportOptions DefaultTransportOptions = new()
    {
        Endpoint = new("http://localhost:5000/sse"),
        Name = "In-memory SSE Client",
    };

    private Task<McpClient> ConnectMcpClientAsync(HttpClient? httpClient = null, HttpClientTransportOptions? transportOptions = null)
        => McpClient.CreateAsync(
            new HttpClientTransport(transportOptions ?? DefaultTransportOptions, httpClient ?? HttpClient, LoggerFactory),
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectMcpClientAsync();

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/message", new Envelope { Message = "Hello, SSE!" }, serializerOptions: JsonContext.Default.Options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveMessage_InMemoryServer_WithFullEndpointEventUri()
    {
        await using var app = Builder.Build();
        MapAbsoluteEndpointUriMcp(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectMcpClientAsync();

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/message", new Envelope { Message = "Hello, SSE!" }, serializerOptions: JsonContext.Default.Options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveMessage_ServerReturningJsonInPostRequest()
    {
        await using var app = Builder.Build();
        MapAbsoluteEndpointUriMcp(app, respondInJson: true);

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectMcpClientAsync();

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/message", new Envelope { Message = "Hello, SSE!" }, serializerOptions: JsonContext.Default.Options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(true);
    }

    [Fact]
    public async Task ConnectAndReceiveNotification_InMemoryServer()
    {
        var receivedNotification = new TaskCompletionSource<string?>();

        Builder.Services.AddMcpServer()
            .WithHttpTransport(httpTransportOptions =>
            {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                httpTransportOptions.RunSessionHandler = (httpContext, mcpServer, cancellationToken) =>
                {
                    // We could also use ServerCapabilities.NotificationHandlers, but it's good to have some test coverage of RunSessionHandler.
                    mcpServer.RegisterNotificationHandler("test/notification", async (notification, cancellationToken) =>
                    {
                        Assert.Equal("Hello from client!", notification.Params?["message"]?.GetValue<string>());
                        await mcpServer.SendNotificationAsync("test/notification", new Envelope { Message = "Hello from server!" }, serializerOptions: JsonContext.Default.Options, cancellationToken);
                    });
                    return mcpServer.RunAsync(cancellationToken);
                };
#pragma warning restore MCPEXP002
            });

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectMcpClientAsync();

        mcpClient.RegisterNotificationHandler("test/notification", (args, ca) =>
        {
            var msg = args.Params?["message"]?.GetValue<string>();
            receivedNotification.SetResult(msg);
            return default;
        });

        // Send a test message through POST endpoint
        await mcpClient.SendNotificationAsync("test/notification", new Envelope { Message = "Hello from client!" }, serializerOptions: JsonContext.Default.Options, cancellationToken: TestContext.Current.CancellationToken);

        var message = await receivedNotification.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.Equal("Hello from server!", message);
    }

    [Fact]
    public async Task AddMcpServer_CanBeCalled_MultipleTimes()
    {
        var firstOptionsCallbackCallCount = 0;
        var secondOptionsCallbackCallCount = 0;

        Builder.Services.AddMcpServer(options =>
            {
                firstOptionsCallbackCallCount++;
            })
            .WithHttpTransport()
            .WithTools<EchoTool>();

        Builder.Services.AddMcpServer(options =>
            {
                secondOptionsCallbackCallCount++;
            })
            .WithTools<SampleLlmTool>();


        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectMcpClientAsync();

        // Options can be lazily initialized, but they must be instantiated by the time an MCP client can finish connecting.
        // Callbacks can be called multiple times if configureOptionsAsync is configured, because that uses the IOptionsFactory,
        // but that's not the case in this test.
        Assert.Equal(1, firstOptionsCallbackCallCount);
        Assert.Equal(1, secondOptionsCallbackCallCount);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tools => tools.Name == "echo");
        Assert.Contains(tools, tools => tools.Name == "sampleLLM");

        var echoResponse = await mcpClient.CallToolAsync(
            "echo",
            new Dictionary<string, object?>
            {
                ["message"] = "from client!"
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var textContent = Assert.Single(echoResponse.Content.OfType<TextContentBlock>());

        Assert.Equal("hello from client!", textContent.Text);
    }

    [Fact]
    public async Task AdditionalHeaders_AreSent_InGetAndPostRequests()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport();

        await using var app = Builder.Build();

        bool wasGetRequest = false;
        bool wasPostRequest = false;

        app.Use(next =>
        {
            return async context =>
            {
                Assert.Equal("Bearer testToken", context.Request.Headers["Authorize"]);
                if (context.Request.Method == HttpMethods.Get)
                {
                    wasGetRequest = true;
                }
                else if (context.Request.Method == HttpMethods.Post)
                {
                    wasPostRequest = true;
                }
                await next(context);
            };
        });

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var sseOptions = new HttpClientTransportOptions
        {
            Endpoint = new("http://localhost:5000/sse"),
            Name = "In-memory SSE Client",
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorize"] = "Bearer testToken"
            },
        };

        await using var mcpClient = await ConnectMcpClientAsync(transportOptions: sseOptions);

        Assert.True(wasGetRequest);
        Assert.True(wasPostRequest);
    }

    [Fact]
    public async Task EmptyAdditionalHeadersKey_Throws_InvalidOperationException()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var sseOptions = new HttpClientTransportOptions
        {
            Endpoint = new("http://localhost:5000/sse"),
            Name = "In-memory SSE Client",
            AdditionalHeaders = new Dictionary<string, string>()
            {
                [""] = ""
            },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectMcpClientAsync(transportOptions: sseOptions));
        Assert.Equal("Failed to add header '' with value '' from AdditionalHeaders.", ex.Message);
    }

    private static void MapAbsoluteEndpointUriMcp(IEndpointRouteBuilder endpoints, bool respondInJson = false)
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

            await using var transport = new SseResponseStreamTransport(response.Body, "http://localhost:5000/message");
            session = transport;

            try
            {
                var transportTask = transport.RunAsync(cancellationToken: requestAborted);
                await using var server = McpServer.Create(transport, optionsSnapshot.Value, loggerFactory, endpoints.ServiceProvider);

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
            var message = await context.Request.ReadFromJsonAsync<JsonRpcMessage>(McpJsonUtilities.DefaultOptions, context.RequestAborted);
            if (message is null)
            {
                await Results.BadRequest("No message in request body.").ExecuteAsync(context);
                return;
            }

            await session.OnMessageReceivedAsync(message, context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status202Accepted;

            if (respondInJson)
            {
                await context.Response.WriteAsJsonAsync(message, McpJsonUtilities.DefaultOptions, cancellationToken: context.RequestAborted);
            }
            else
            {
                await context.Response.WriteAsync("Accepted");
            }
        });
    }

    public class Envelope
    {
        public required string Message { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Envelope))]
    partial class JsonContext : JsonSerializerContext;
}
