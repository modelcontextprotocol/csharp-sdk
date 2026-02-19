using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

public abstract class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    protected abstract bool UseStreamableHttp { get; }
    protected abstract bool Stateless { get; }

    protected void ConfigureStateless(HttpServerTransportOptions options)
    {
        options.Stateless = Stateless;
    }

    protected async Task<McpClient> ConnectAsync(
        string? path = null,
        HttpClientTransportOptions? transportOptions = null,
        McpClientOptions? clientOptions = null)
    {
        // Default behavior when no options are provided
        path ??= UseStreamableHttp ? "/" : "/sse";

        await using var transport = new HttpClientTransport(transportOptions ?? new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://localhost:5000{path}"),
            TransportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse,
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, clientOptions, LoggerFactory, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MapMcp_ThrowsInvalidOperationException_IfWithHttpTransportIsNotCalled()
    {
        Builder.Services.AddMcpServer();
        await using var app = Builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => app.MapMcp());
        Assert.StartsWith("You must call WithHttpTransport()", exception.Message);
    }

    [Fact]
    public async Task Can_UseIHttpContextAccessor_InTool()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                context.User = CreateUser("TestUser");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        var response = await mcpClient.CallToolAsync(
            "echo_with_user_name",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content.OfType<TextContentBlock>());
        Assert.Equal("TestUser: Hello world!", content.Text);
    }

    [Fact]
    public async Task Messages_FromNewUser_AreRejected()
    {
        Assert.SkipWhen(Stateless, "User validation across requests is not applicable in stateless mode.");

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        // Add an authentication scheme that will send a 403 Forbidden response.
        Builder.Services.AddAuthentication().AddBearerToken();
        Builder.Services.AddHttpContextAccessor();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            var i = 0;
            return async context =>
            {
                context.User = CreateUser($"TestUser{Interlocked.Increment(ref i)}");
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var httpRequestException = await Assert.ThrowsAsync<HttpRequestException>(() => ConnectAsync());
        Assert.Equal(HttpStatusCode.Forbidden, httpRequestException.StatusCode);
    }

    [Fact]
    public async Task ClaimsPrincipal_CanBeInjected_IntoToolMethod()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();

        app.Use(next => async context =>
        {
            context.User = CreateUser("TestUser");
            await next(context);
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync();

        var response = await client.CallToolAsync(
            "echo_claims_principal",
            new Dictionary<string, object?>() { ["message"] = "Hello world!" },
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(response.Content.OfType<TextContentBlock>());
        Assert.Equal("TestUser: Hello world!", content.Text);
    }

    [Fact]
    public async Task Sampling_DoesNotCloseStreamPrematurely()
    {
        Assert.SkipWhen(Stateless, "Sampling is not supported in stateless mode.");

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<SamplingRegressionTools>();

        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var sampleCount = 0;
        var clientOptions = new McpClientOptions()
        {
            Handlers = new()
            {
                SamplingHandler = async (parameters, _, _) =>
                {
                    Assert.NotNull(parameters?.Messages);
                    var message = Assert.Single(parameters.Messages);
                    Assert.Equal(Role.User, message.Role);
                    Assert.Equal("Test prompt for sampling", Assert.IsType<TextContentBlock>(Assert.Single(message.Content)).Text);

                    sampleCount++;
                    return new CreateMessageResult
                    {
                        Model = "test-model",
                        Role = Role.Assistant,
                        Content = [new TextContentBlock { Text = "Sampling response from client" }],
                    };
                }
            }
        };

        await using var mcpClient = await ConnectAsync(clientOptions: clientOptions);

        var result = await mcpClient.CallToolAsync("sampling-tool", new Dictionary<string, object?>
        {
            ["prompt"] = "Test prompt for sampling"
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.IsError);
        var textContent = Assert.Single(result.Content);
        Assert.Equal("text", textContent.Type);
        Assert.Equal("Sampling completed successfully. Client responded: Sampling response from client", Assert.IsType<TextContentBlock>(textContent).Text);

        Assert.Equal(2, sampleCount);

        // Verify that the tool call and the sampling request both used the same ID to ensure we cover against regressions.
        // https://github.com/modelcontextprotocol/csharp-sdk/issues/464
        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.Category == "ModelContextProtocol.Client.McpClient" &&
            m.Message.Contains("request '2' for method 'tools/call'"));

        Assert.Single(MockLoggerProvider.LogMessages, m =>
            m.Category == "ModelContextProtocol.Server.McpServer" &&
            m.Message.Contains("request '2' for method 'sampling/createMessage'"));
    }

    [Fact]
    public async Task Server_ShutsDownQuickly_WhenClientIsConnected()
    {
        Builder.Services.AddMcpServer().WithHttpTransport().WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect a client which will open a long-running GET request (SSE or Streamable HTTP)
        await using var mcpClient = await ConnectAsync();

        // Verify the client is connected
        var tools = await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);

        // Now measure how long it takes to stop the server
        var stopwatch = Stopwatch.StartNew();
        await app.StopAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        // The server should shut down quickly (within a few seconds). We use 5 seconds as a generous threshold.
        // This is much less than the default HostOptions.ShutdownTimeout of 30 seconds.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Server took {stopwatch.Elapsed.TotalSeconds:F2} seconds to shut down with a connected client. " +
            "This suggests the GET request is not respecting ApplicationStopping token.");
    }

    [Fact]
    public async Task LongRunningToolCall_DoesNotTimeout_WhenNoEventStreamStore()
    {
        // Regression test for: Tool calls that last over HttpClient timeout without producing
        // intermediate notifications will timeout because HttpClient doesn't see the 200 response
        // until the first message is written. When primingItem is null (no ISseEventStreamStore),
        // we should flush the response stream so HttpClient sees the 200 immediately.

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<LongRunningTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Retry a couple of times to reduce occasional flakiness on low-resource machines.
        // If the server regresses to flushing only after tool completion, each attempt should still fail
        // because HttpClient timeout (1 second) is below the tool duration (2 seconds).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // Create a custom HttpClient with a very short timeout (1 second)
                // The tool will take 2 seconds to complete
                using var shortTimeoutClient = new HttpClient(SocketsHttpHandler, disposeHandler: false)
                {
                    BaseAddress = new Uri("http://localhost:5000/"),
                    Timeout = TimeSpan.FromSeconds(1)
                };

                var path = UseStreamableHttp ? "/" : "/sse";
                var transportMode = UseStreamableHttp ? HttpTransportMode.StreamableHttp : HttpTransportMode.Sse;

                await using var transport = new HttpClientTransport(new()
                {
                    Endpoint = new($"http://localhost:5000{path}"),
                    TransportMode = transportMode,
                }, shortTimeoutClient, LoggerFactory);

                await using var mcpClient = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

                // Call a tool that takes 2 seconds - this should succeed despite the 1 second HttpClient timeout
                // because the response stream is flushed immediately after receiving the request
                var response = await mcpClient.CallToolAsync(
                    "long_running_operation",
                    new Dictionary<string, object?>() { ["durationMs"] = 2000 },
                    cancellationToken: TestContext.Current.CancellationToken);

                var content = Assert.Single(response.Content.OfType<TextContentBlock>());
                Assert.Equal("Operation completed after 2000ms", content.Text);
                return;
            }
            catch (OperationCanceledException) when (attempt < 2)
            {
                // Retry intermittent timeout-related failures on slow CI machines.
            }
        }

    }

    private ClaimsPrincipal CreateUser(string name)
        => new(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name)],
            "TestAuthType", "name", "role"));

    [McpServerToolType]
    protected class EchoHttpContextUserTools(IHttpContextAccessor contextAccessor)
    {
        [McpServerTool, Description("Echoes the input back to the client with their user name.")]
        public string EchoWithUserName(string message)
        {
            var httpContext = contextAccessor.HttpContext ?? throw new Exception("HttpContext unavailable!");
            var userName = httpContext.User.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }

    [McpServerToolType]
    protected class ClaimsPrincipalTools
    {
        [McpServerTool, Description("Echoes the input back to the client with the user name from ClaimsPrincipal.")]
        public string EchoClaimsPrincipal(ClaimsPrincipal? user, string message)
        {
            var userName = user?.Identity?.Name ?? "anonymous";
            return $"{userName}: {message}";
        }
    }

    [McpServerToolType]
    private class SamplingRegressionTools
    {
        [McpServerTool(Name = "sampling-tool")]
        public static async Task<string> SamplingToolAsync(McpServer server, string prompt, CancellationToken cancellationToken)
        {
            // This tool reproduces the scenario described in https://github.com/modelcontextprotocol/csharp-sdk/issues/464
            // 1. The client calls tool with request ID 2, because it's the first request after the initialize request.
            // 2. This tool makes two sampling requests which use IDs 1 and 2.
            // 3. In the old buggy Streamable HTTP transport code, this would close the SSE response stream,
            //    because the second sampling request used an ID matching the tool call.
            var samplingRequest = new CreateMessageRequestParams
            {
                Messages = [
                    new SamplingMessage
                    {
                        Role = Role.User,
                        Content = [new TextContentBlock { Text = prompt }],
                    }
                ],
                MaxTokens = 1000
            };

            await server.SampleAsync(samplingRequest, cancellationToken);
            var samplingResult = await server.SampleAsync(samplingRequest, cancellationToken);

            return $"Sampling completed successfully. Client responded: {Assert.IsType<TextContentBlock>(Assert.Single(samplingResult.Content)).Text}";
        }
    }

    [McpServerToolType]
    protected class LongRunningTools
    {
        [McpServerTool, Description("Simulates a long-running operation")]
        public static async Task<string> LongRunningOperation(
            [Description("Duration of the operation in milliseconds")] int durationMs,
            CancellationToken cancellationToken)
        {
            await Task.Delay(durationMs, cancellationToken);
            return $"Operation completed after {durationMs}ms";
        }
    }
}
