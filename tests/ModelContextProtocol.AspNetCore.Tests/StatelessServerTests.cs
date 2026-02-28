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
    public async Task ScopedServices_AccessibleInToolHandler_AfterConnectionAbort()
    {
        // Regression test for https://github.com/modelcontextprotocol/csharp-sdk/issues/1269
        // Verifies that scoped services (like DbContext) remain accessible in tool handlers
        // even when the HTTP connection is aborted before the handler completes.
        var abortTestState = new AbortTestState();

        Builder.Services.AddMcpServer(mcpServerOptions =>
            {
                mcpServerOptions.ServerInfo = new Implementation
                {
                    Name = nameof(StatelessServerTests),
                    Version = "73",
                };
            })
            .WithHttpTransport(httpServerTransportOptions =>
            {
                httpServerTransportOptions.Stateless = true;
            })
            .WithTools<StatelessServerTests>();

        Builder.Services.AddScoped<ScopedService>();
        Builder.Services.AddSingleton(abortTestState);

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

        await using var client = await ConnectMcpClientAsync();

        using var cts = new CancellationTokenSource();

        // Start the tool call - it will block in the handler until we release ContinueToolExecution.
        var callTask = client.CallToolAsync("testAbortedConnectionScope", cancellationToken: cts.Token);

        // Wait for the handler to start executing.
        await abortTestState.ToolStarted.WaitAsync(TestContext.Current.CancellationToken);

        // Abort the connection by cancelling the token. This triggers context.RequestAborted
        // on the server, which starts the session disposal chain.
        await cts.CancelAsync();

        // Let the handler continue - it will now try to access the scoped service.
        // Before the fix, this would throw ObjectDisposedException because the request scope
        // was disposed when the HTTP connection was aborted.
        abortTestState.ContinueToolExecution.Release();

        // Verify through the side channel that the handler accessed the scoped service
        // without ObjectDisposedException. The client call itself was aborted, so we can't
        // rely on the return value.
        var result = await abortTestState.ScopeAccessResult.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal("From request middleware!", result);

        // Clean up the aborted client call.
        try { await callTask; } catch { }
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

    [McpServerTool(Name = "testAbortedConnectionScope")]
    public static async Task<string?> TestAbortedConnectionScope(ScopedService scopedService, AbortTestState abortTestState)
    {
        // Signal the test that the handler has started.
        abortTestState.ToolStarted.Release();

        // Wait for the test to abort the connection. Use CancellationToken.None so this
        // handler continues executing even after the HTTP request is aborted.
        await abortTestState.ContinueToolExecution.WaitAsync(CancellationToken.None);

        try
        {
            // Access the scoped service AFTER the connection was aborted.
            // Before the fix for https://github.com/modelcontextprotocol/csharp-sdk/issues/1269,
            // this would throw ObjectDisposedException because ASP.NET Core disposed
            // the request's IServiceProvider while the handler was still executing.
            var result = scopedService.State;
            abortTestState.ScopeAccessResult.TrySetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            abortTestState.ScopeAccessResult.TrySetException(ex);
            throw;
        }
    }

    public class AbortTestState
    {
        public SemaphoreSlim ToolStarted { get; } = new(0, 1);
        public SemaphoreSlim ContinueToolExecution { get; } = new(0, 1);
        public TaskCompletionSource<string?> ScopeAccessResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public class ScopedService
    {
        public string? State { get; set; }
    }
}
