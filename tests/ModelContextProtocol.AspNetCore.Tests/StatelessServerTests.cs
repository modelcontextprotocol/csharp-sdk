using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ModelContextProtocol.AspNetCore.Tests;

[McpServerToolType]
public class StatelessServerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private readonly HttpClientTransportOptions DefaultTransportOptions = new()
    {
        Endpoint = new("http://localhost:5000/"),
        Name = "In-memory Streamable HTTP Client",
        TransportMode = HttpTransportMode.StreamableHttp,
    };

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(mcpServerOptions =>
            {
                mcpServerOptions.ServerInfo = new Implementation
                {
                    Name = nameof(StreamableHttpServerConformanceTests),
                    Version = "73",
                };
            })
            .WithHttpTransport(httpServerTransportOptions =>
            {
                httpServerTransportOptions.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools<StatelessServerTests>();

        Builder.Services.AddScoped<ScopedService>();

        _app = Builder.Build();

        _app.Use(next =>
        {
            return context =>
            {
                context.RequestServices.GetRequiredService<ScopedService>().State = "From request middleware!";
                return next(context);
            };
        });

        _app.MapMcp();

        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    private Task<McpClient> ConnectMcpClientAsync(McpClientOptions? clientOptions = null)
        => McpClient.CreateAsync(
            new HttpClientTransport(DefaultTransportOptions, HttpClient, LoggerFactory),
            clientOptions, LoggerFactory, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [Fact]
    public async Task EnablingStatelessMode_Disables_SseEndpoints()
    {
        await StartAsync();

        using var sseResponse = await HttpClient.GetAsync("/sse", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, sseResponse.StatusCode);

        using var messageResponse = await HttpClient.PostAsync("/message", new StringContent(""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, messageResponse.StatusCode);
    }

    [Fact]
    public async Task EnablingStatelessMode_Disables_GetAndDeleteEndpoints()
    {
        await StartAsync();

        using var getResponse = await HttpClient.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResponse.StatusCode);

        using var deleteResponse = await HttpClient.DeleteAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task SamplingRequest_Fails_WithInvalidOperationException()
    {
        await StartAsync();

        var mcpClientOptions = new McpClientOptions();
        mcpClientOptions.Handlers.SamplingHandler = (_, _, _) =>
        {
            throw new UnreachableException();
        };

        await using var client = await ConnectMcpClientAsync(mcpClientOptions);

        var toolResponse = await client.CallToolAsync("testSamplingErrors", cancellationToken: TestContext.Current.CancellationToken);
        var toolContent = Assert.Single(toolResponse.Content);
        Assert.Equal("Server to client requests are not supported in stateless mode.", Assert.IsType<TextContentBlock>(toolContent).Text);
    }

    [Fact]
    public async Task RootsRequest_Fails_WithInvalidOperationException()
    {
        await StartAsync();

        var mcpClientOptions = new McpClientOptions();
        mcpClientOptions.Handlers.RootsHandler = (_, _) =>
        {
            throw new UnreachableException();
        };

        await using var client = await ConnectMcpClientAsync(mcpClientOptions);

        var toolResponse = await client.CallToolAsync("testRootsErrors", cancellationToken: TestContext.Current.CancellationToken);
        var toolContent = Assert.Single(toolResponse.Content);
        Assert.Equal("Server to client requests are not supported in stateless mode.", Assert.IsType<TextContentBlock>(toolContent).Text);
    }

    [Fact]
    public async Task ElicitRequest_Fails_WithInvalidOperationException()
    {
        await StartAsync();

        var mcpClientOptions = new McpClientOptions();
        mcpClientOptions.Handlers.ElicitationHandler = (_, _) =>
        {
            throw new UnreachableException();
        };

        await using var client = await ConnectMcpClientAsync(mcpClientOptions);

        var toolResponse = await client.CallToolAsync("testElicitationErrors", cancellationToken: TestContext.Current.CancellationToken);
        var toolContent = Assert.Single(toolResponse.Content);
        Assert.Equal("Server to client requests are not supported in stateless mode.", Assert.IsType<TextContentBlock>(toolContent).Text);
    }

    [Fact]
    public async Task UnsolicitedNotification_Fails_WithInvalidOperationException()
    {
        InvalidOperationException? unsolicitedNotificationException = null;

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
                options.RunSessionHandler = async (context, server, cancellationToken) =>
                {
                    unsolicitedNotificationException = await Assert.ThrowsAsync<InvalidOperationException>(
                        () => server.SendNotificationAsync(NotificationMethods.PromptListChangedNotification, TestContext.Current.CancellationToken));

                    await server.RunAsync(cancellationToken);
                };
#pragma warning restore MCPEXP002
            });

        await StartAsync();

        await using var client = await ConnectMcpClientAsync();

        Assert.NotNull(unsolicitedNotificationException);
        Assert.Equal("Unsolicited server to client messages are not supported in stateless mode.", unsolicitedNotificationException.Message);
    }

    [Fact]
    public async Task ScopedServices_Resolve_FromRequestScope()
    {
        await StartAsync();

        await using var client = await ConnectMcpClientAsync();

        var toolResponse = await client.CallToolAsync("testScope", cancellationToken: TestContext.Current.CancellationToken);
        var toolContent = Assert.Single(toolResponse.Content);
        Assert.Equal("From request middleware!", Assert.IsType<TextContentBlock>(toolContent).Text);
    }

    [Fact]
    public async Task ProgressNotifications_Work_InStatelessMode()
    {
        // Use TCS to coordinate: the tool reports progress, then waits for the test to confirm
        // the notification arrived before completing. This avoids the race where fire-and-forget
        // NotifyProgressAsync hasn't flushed before the SSE stream closes.
        var progressReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolCanComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools([McpServerTool.Create(
                async (IProgress<ProgressNotificationValue> progress) =>
                {
                    progress.Report(new() { Progress = 0, Total = 1, Message = "Working" });
                    await toolCanComplete.Task;
                    return "complete";
                }, new() { Name = "progressTool" })]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        await using var client = await ConnectMcpClientAsync();

        // Use a custom IProgress<T> that sets the TCS synchronously (no thread pool posting).
        var callTask = client.CallToolAsync(
            "progressTool",
            progress: new SynchronousProgress<ProgressNotificationValue>(_ => progressReceived.TrySetResult()),
            cancellationToken: TestContext.Current.CancellationToken);

        // Wait for the progress notification to arrive at the client.
        await progressReceived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // Let the tool complete now that we've confirmed progress was received.
        toolCanComplete.SetResult();

        var toolResponse = await callTask;
        var content = Assert.Single(toolResponse.Content);
        Assert.Equal("complete", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task ConfigureSessionOptions_RunsPerRequest_InStatelessMode()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
                options.ConfigureSessionOptions = (httpContext, mcpServerOptions, cancellationToken) =>
                {
                    // Dynamically add a tool based on a request header value.
                    var toolSuffix = httpContext.Request.Headers["X-Tool-Suffix"].ToString();
                    if (!string.IsNullOrEmpty(toolSuffix))
                    {
                        mcpServerOptions.ToolCollection =
                        [
                            McpServerTool.Create(() => $"configured-{toolSuffix}", new() { Name = "dynamicTool" })
                        ];
                    }

                    return Task.CompletedTask;
                };
            });

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        // Two separate McpClient instances are needed because the X-Tool-Suffix header is set on
        // the shared HttpClient before connecting. Each McpClient captures the headers at connect
        // time, so changing headers between clients proves ConfigureSessionOptions sees different
        // request data on each HTTP request.

        // First request with "alpha" — proves ConfigureSessionOptions runs and configures the tool.
        HttpClient.DefaultRequestHeaders.Add("X-Tool-Suffix", "alpha");

        await using var client1 = await ConnectMcpClientAsync();

        var toolResponse1 = await client1.CallToolAsync("dynamicTool", cancellationToken: TestContext.Current.CancellationToken);
        var content1 = Assert.Single(toolResponse1.Content);
        Assert.Equal("configured-alpha", Assert.IsType<TextContentBlock>(content1).Text);

        // Second request with "beta" — proves ConfigureSessionOptions runs again with new request data.
        HttpClient.DefaultRequestHeaders.Remove("X-Tool-Suffix");
        HttpClient.DefaultRequestHeaders.Add("X-Tool-Suffix", "beta");

        await using var client2 = await ConnectMcpClientAsync();

        var toolResponse2 = await client2.CallToolAsync("dynamicTool", cancellationToken: TestContext.Current.CancellationToken);
        var content2 = Assert.Single(toolResponse2.Content);
        Assert.Equal("configured-beta", Assert.IsType<TextContentBlock>(content2).Text);
    }

    [Fact]
    public async Task StatelessMode_DoesNotAdvertise_ListChangedCapabilities()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools([McpServerTool.Create(() => "result", new() { Name = "myTool" })])
            .WithPrompts([McpServerPrompt.Create(() => new GetPromptResult(), new() { Name = "myPrompt" })])
            .WithResources([McpServerResource.Create(() => new ReadResourceResult(), new() { UriTemplate = "resource://test" })]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        await using var client = await ConnectMcpClientAsync();

        Assert.Null(client.ServerCapabilities.Tools?.ListChanged);
        Assert.Null(client.ServerCapabilities.Prompts?.ListChanged);
        Assert.Null(client.ServerCapabilities.Resources?.ListChanged);
    }

    [Fact]
    public async Task SubscriptionsListen_InStatelessMode_GrantsNothing_AndDoesNotHoldRequestOpen()
    {
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools([McpServerTool.Create(() => "result", new() { Name = "myTool" })])
            .WithPrompts([McpServerPrompt.Create(() => new GetPromptResult(), new() { Name = "myPrompt" })])
            .WithResources([McpServerResource.Create(() => new ReadResourceResult(), new() { UriTemplate = "resource://test" })]);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        await using var client = await ConnectMcpClientAsync();

        var ackChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var ackReg = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { ackChannel.Writer.TryWrite(notification); return default; });

        // Request every kind of subscription the protocol exposes, even though the server registers
        // subscribable primitives. A stateless session cannot push out-of-band notifications, so the
        // request must acknowledge with no grants and complete promptly instead of holding the POST
        // (and its request scope) open forever - a regression would hang here until the timeout.
        var listenRequest = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams
                {
                    Notifications = new SubscriptionsListenNotifications
                    {
                        ToolsListChanged = true,
                        PromptsListChanged = true,
                        ResourcesListChanged = true,
                        ResourceSubscriptions = ["resource://test"],
                    },
                },
                McpJsonUtilities.DefaultOptions),
        };

        await client.SendRequestAsync(listenRequest, TestContext.Current.CancellationToken)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // The acknowledgement is sent before the response completes, so it is already buffered here.
        var ack = await ackChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var grantedNotifications = Assert.IsType<JsonObject>(Assert.IsType<JsonObject>(ack.Params)["notifications"]);
        Assert.Null(grantedNotifications["toolsListChanged"]);
        Assert.Null(grantedNotifications["promptsListChanged"]);
        Assert.Null(grantedNotifications["resourcesListChanged"]);
        Assert.Null(grantedNotifications["resourceSubscriptions"]);
    }

    [Fact]
    public async Task SubscriptionsListen_WithCustomHandler_InStatelessMode_StreamsNotificationOverHeldOpenPost()
    {
        // The built-in stateless handler grants nothing and returns immediately because there is no
        // session-wide channel. A custom SubscriptionsListenHandler can instead stream notifications over the
        // held-open POST response (the listen request's RelatedTransport), which is the solicited
        // server-to-client stream. This is the core scenario of issue #1662.
        const string subscribedUri = "resource://test";

        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithSubscriptionsListenHandler(async (request, cancellationToken) =>
            {
                var subscriptionId = request.JsonRpcRequest.Id;

                var ack = new JsonRpcNotification
                {
                    Method = NotificationMethods.SubscriptionsAcknowledgedNotification,
                    Params = JsonSerializer.SerializeToNode(
                        new SubscriptionsAcknowledgedNotificationParams
                        {
                            Notifications = new SubscriptionsListenNotifications
                            {
                                ResourceSubscriptions = request.Params.Notifications.ResourceSubscriptions,
                            },
                        },
                        McpJsonUtilities.DefaultOptions),
                };
                TagWithSubscriptionId(ack, subscriptionId);
                await request.Server.SendMessageAsync(ack, cancellationToken);

                var updated = new JsonRpcNotification
                {
                    Method = NotificationMethods.ResourceUpdatedNotification,
                    Params = new JsonObject { ["uri"] = subscribedUri },
                };
                TagWithSubscriptionId(updated, subscriptionId);
                await request.Server.SendMessageAsync(updated, cancellationToken);

                // Complete the stream so the POST response finishes; the buffered notifications flush to the client.
                return new EmptyResult();
            });

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        await using var client = await ConnectMcpClientAsync();

        var ackChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        var updatedChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var ackReg = client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification,
            (notification, _) => { ackChannel.Writer.TryWrite(notification); return default; });
        await using var updatedReg = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification,
            (notification, _) => { updatedChannel.Writer.TryWrite(notification); return default; });

        var listenRequest = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams
                {
                    Notifications = new SubscriptionsListenNotifications { ResourceSubscriptions = [subscribedUri] },
                },
                McpJsonUtilities.DefaultOptions),
        };

        await client.SendRequestAsync(listenRequest, TestContext.Current.CancellationToken)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        var ack = await ackChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var subscriptionId = GetSubscriptionId(ack);
        Assert.NotNull(subscriptionId);

        var updated = await updatedChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(subscriptionId, GetSubscriptionId(updated));
        Assert.Equal(subscribedUri, Assert.IsType<JsonObject>(updated.Params)["uri"]?.GetValue<string>());
    }

    [Fact]
    public async Task SubscriptionsListen_WithCustomHandler_InStatelessMode_AdvertisesAndStreamsListChanged()
    {
        // resources/updated rides on resources.subscribe, which is never suppressed, so it cannot prove the
        // listChanged capability is advertised. A custom SubscriptionsListenHandler gives a stateless server a
        // way to deliver */list_changed over the held-open POST, so server/discover (the only path a
        // 2026-07-28+ client uses) must advertise tools.listChanged rather than dropping it (issue #1662).
        Builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.Stateless;
            })
            .WithTools([McpServerTool.Create(() => "result", new() { Name = "myTool" })])
            .WithSubscriptionsListenHandler(async (request, cancellationToken) =>
            {
                var subscriptionId = request.JsonRpcRequest.Id;

                var ack = new JsonRpcNotification
                {
                    Method = NotificationMethods.SubscriptionsAcknowledgedNotification,
                    Params = JsonSerializer.SerializeToNode(
                        new SubscriptionsAcknowledgedNotificationParams
                        {
                            Notifications = new SubscriptionsListenNotifications
                            {
                                ToolsListChanged = request.Params.Notifications.ToolsListChanged,
                            },
                        },
                        McpJsonUtilities.DefaultOptions),
                };
                TagWithSubscriptionId(ack, subscriptionId);
                await request.Server.SendMessageAsync(ack, cancellationToken);

                var listChanged = new JsonRpcNotification { Method = NotificationMethods.ToolListChangedNotification };
                TagWithSubscriptionId(listChanged, subscriptionId);
                await request.Server.SendMessageAsync(listChanged, cancellationToken);

                return new EmptyResult();
            });

        // Advertise tools.listChanged so the per-response capability decision has something to preserve.
        Builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Capabilities ??= new();
            options.Capabilities.Tools ??= new();
            options.Capabilities.Tools.ListChanged = true;
        });

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        await using var client = await ConnectMcpClientAsync();

        // The stateless server can now deliver tools/list_changed over the custom listen stream, so the
        // capability must survive on the server/discover response instead of being cleared.
        Assert.True(client.ServerCapabilities.Tools?.ListChanged);

        var listChangedChannel = Channel.CreateUnbounded<JsonRpcNotification>();
        await using var listChangedReg = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification,
            (notification, _) => { listChangedChannel.Writer.TryWrite(notification); return default; });

        var listenRequest = new JsonRpcRequest
        {
            Method = RequestMethods.SubscriptionsListen,
            Params = JsonSerializer.SerializeToNode(
                new SubscriptionsListenRequestParams
                {
                    Notifications = new SubscriptionsListenNotifications { ToolsListChanged = true },
                },
                McpJsonUtilities.DefaultOptions),
        };

        await client.SendRequestAsync(listenRequest, TestContext.Current.CancellationToken)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        var listChangedNotification = await listChangedChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(GetSubscriptionId(listChangedNotification));
    }

    private static string? GetSubscriptionId(JsonRpcNotification notification)
        => ((notification.Params as JsonObject)?["_meta"] as JsonObject)?[MetaKeys.SubscriptionId]?.ToJsonString();

    private static void TagWithSubscriptionId(JsonRpcNotification notification, RequestId subscriptionId)
    {
        var paramsObject = notification.Params as JsonObject ?? new JsonObject();
        if (paramsObject["_meta"] is not JsonObject meta)
        {
            meta = new JsonObject();
            paramsObject["_meta"] = meta;
        }

        meta[MetaKeys.SubscriptionId] = subscriptionId.Id switch
        {
            string stringId => JsonValue.Create(stringId),
            long longId => JsonValue.Create(longId),
            _ => null,
        };

        notification.Params = paramsObject;
    }

    [McpServerTool(Name = "testSamplingErrors")]
    public static async Task<string> TestSamplingErrors(McpServer server)
    {
        const string expectedSamplingErrorMessage = "Sampling is not supported in stateless mode.";

        // Even when the client has sampling support, it should not be advertised in stateless mode.
        Assert.Null(server.ClientCapabilities);

        var asSamplingChatClientEx = Assert.Throws<InvalidOperationException>(() => server.AsSamplingChatClient());
        Assert.Equal(expectedSamplingErrorMessage, asSamplingChatClientEx.Message);

        var requestSamplingEx = await Assert.ThrowsAsync<InvalidOperationException>(() => server.SampleAsync([]));
        Assert.Equal(expectedSamplingErrorMessage, requestSamplingEx.Message);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendRequestAsync(new JsonRpcRequest
        {
            Method = RequestMethods.SamplingCreateMessage
        }));
        return ex.Message;
    }

    [McpServerTool(Name = "testRootsErrors")]
    public static async Task<string> TestRootsErrors(McpServer server)
    {
        const string expectedRootsErrorMessage = "Roots are not supported in stateless mode.";

        // Even when the client has roots support, it should not be advertised in stateless mode.
        Assert.Null(server.ClientCapabilities);

        var requestRootsEx = Assert.Throws<InvalidOperationException>(() => server.RequestRootsAsync(new()));
        Assert.Equal(expectedRootsErrorMessage, requestRootsEx.Message);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendRequestAsync(new JsonRpcRequest
        {
            Method = RequestMethods.RootsList
        }));
        return ex.Message;
    }

    [McpServerTool(Name = "testElicitationErrors")]
    public static async Task<string> TestElicitationErrors(McpServer server)
    {
        const string expectedElicitationErrorMessage = "Elicitation is not supported in stateless mode.";

        // Even when the client has elicitation support, it should not be advertised in stateless mode.
        Assert.Null(server.ClientCapabilities);

        var requestElicitationEx = await Assert.ThrowsAsync<InvalidOperationException>(() => server.ElicitAsync(new() { Message = string.Empty }).AsTask());
        Assert.Equal(expectedElicitationErrorMessage, requestElicitationEx.Message);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendRequestAsync(new JsonRpcRequest
        {
            Method = RequestMethods.ElicitationCreate
        }));
        return ex.Message;
    }

    [McpServerTool(Name = "testScope")]
    public static string? TestScope(ScopedService scopedService) => scopedService.State;

    public class ScopedService
    {
        public string? State { get; set; }
    }

    private class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
