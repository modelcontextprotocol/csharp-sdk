using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Net;

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
                httpServerTransportOptions.Stateless = true;
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
                options.Stateless = true;
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
                options.Stateless = true;
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
