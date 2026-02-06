using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

public class StreamableHttpClientConformanceTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;
    private readonly List<string> _deleteRequestSessionIds = [];

    // Don't add the delete endpoint by default to ensure the client still works with basic sessionless servers.
    private async Task StartAsync(bool enableDelete = false)
    {
        Builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(McpJsonUtilities.DefaultOptions.TypeInfoResolver!);
        });
        _app = Builder.Build();

        var echoTool = McpServerTool.Create(Echo, new()
        {
            Services = _app.Services,
        });

        _app.MapPost("/mcp", (JsonRpcMessage message, HttpContext context) =>
        {
            if (message is not JsonRpcRequest request)
            {
                // Ignore all non-request notifications.
                return Results.Accepted();
            }

            if (enableDelete)
            {
                // Add a session ID to the response to enable session tracking
                context.Response.Headers.Append("mcp-session-id", "test-session-123");
            }

            if (request.Method == "initialize")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = "2024-11-05",
                        Capabilities = new()
                        {
                            Tools = new(),
                        },
                        ServerInfo = new Implementation
                        {
                            Name = "my-mcp",
                            Version = "0.0.1",
                        },
                    }, McpJsonUtilities.DefaultOptions)
                });
            }

            if (request.Method == "tools/list")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult
                    {
                        Tools = [echoTool.ProtocolTool]
                    }, McpJsonUtilities.DefaultOptions),
                });
            }

            if (request.Method == "tools/call")
            {
                var parameters = JsonSerializer.Deserialize(request.Params, GetJsonTypeInfo<CallToolRequestParams>());
                Assert.NotNull(parameters?.Arguments);

                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = parameters.Arguments["message"].ToString() }],
                    }, McpJsonUtilities.DefaultOptions),
                });
            }

            throw new Exception("Unexpected message!");
        });

        if (enableDelete)
        {
            _app.MapDelete("/mcp", context =>
            {
                _deleteRequestSessionIds.Add(context.Request.Headers["mcp-session-id"].ToString());
                return Task.CompletedTask;
            });
        }

        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    private async Task<ResumeTestServer> StartResumeServerAsync(string expectedSessionId)
    {
        Builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(McpJsonUtilities.DefaultOptions.TypeInfoResolver!);
        });

        _app = Builder.Build();

        var resumeServer = new ResumeTestServer(expectedSessionId);
        resumeServer.MapEndpoints(_app);

        await _app.StartAsync(TestContext.Current.CancellationToken);
        return resumeServer;
    }

    [Fact]
    public async Task CanCallToolOnSessionlessStreamableHttpServer()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var echoTool = Assert.Single(tools);
        Assert.Equal("echo", echoTool.Name);
        await CallEchoAndValidateAsync(echoTool);
    }


    [Fact]
    public async Task CanCallToolConcurrently()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var echoTool = Assert.Single(tools);
        Assert.Equal("echo", echoTool.Name);

        var echoTasks = new Task[100];
        for (int i = 0; i < echoTasks.Length; i++)
        {
            echoTasks[i] = CallEchoAndValidateAsync(echoTool);
        }

        await Task.WhenAll(echoTasks);
    }

    [Fact]
    public async Task SendsDeleteRequestOnDispose()
    {
        await StartAsync(enableDelete: true);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Dispose should trigger DELETE request
        await client.DisposeAsync();

        // Verify DELETE request was sent with correct session ID
        var sessionId = Assert.Single(_deleteRequestSessionIds);
        Assert.Equal("test-session-123", sessionId);
    }

    [Fact]
    public async Task DoesNotSendDeleteWhenTransportDoesNotOwnSession()
    {
        await StartAsync(enableDelete: true);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        await using (await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            // No-op. Disposing the client should not trigger a DELETE request.
        }

        Assert.Empty(_deleteRequestSessionIds);
    }

    [Fact]
    public async Task ResumeSessionStartsGetImmediately()
    {
        const string sessionId = "resume-session-123";
        const string resumeInstructions = "Use cached instructions";
        const string resumeProtocolVersion = "2025-11-25";
        var resumeServer = await StartResumeServerAsync(sessionId);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            KnownSessionId = sessionId,
        }, HttpClient, LoggerFactory);

        var serverCapabilities = new ServerCapabilities
        {
            Tools = new(),
        };
        var resumeOptions = new ResumeClientSessionOptions
        {
            ServerCapabilities = serverCapabilities,
            ServerInfo = new Implementation { Name = "resume-server", Version = "1.0.0" },
            ServerInstructions = resumeInstructions,
            NegotiatedProtocolVersion = resumeProtocolVersion,
        };

        await using (var client = await McpClient.ResumeSessionAsync(
            transport,
            resumeOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            var observedSessionId = await resumeServer.GetStarted.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
            Assert.Equal(sessionId, observedSessionId);

            var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var tool = Assert.Single(tools);
            Assert.Equal("resume-echo", tool.Name);

            Assert.Equal(sessionId, Assert.Single(resumeServer.PostSessionIds));
            Assert.Same(serverCapabilities, client.ServerCapabilities);
            Assert.Same(resumeOptions.ServerInfo, client.ServerInfo);
            Assert.Equal(resumeInstructions, client.ServerInstructions);
            Assert.Equal(resumeProtocolVersion, client.NegotiatedProtocolVersion);
        }

        Assert.Equal(sessionId, Assert.Single(resumeServer.DeleteSessionIds));
    }

    [Fact]
    public async Task CreateAsyncWithKnownSessionIdThrows()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            KnownSessionId = "already-initialized",
        }, HttpClient, LoggerFactory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(nameof(McpClient.ResumeSessionAsync), exception.Message);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotHang_WhenOwnsSessionIsFalse_WithActiveGetStream()
    {
        var getRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(McpJsonUtilities.DefaultOptions.TypeInfoResolver!);
        });
        _app = Builder.Build();

        var echoTool = McpServerTool.Create(Echo, new() { Services = _app.Services });

        _app.MapPost("/mcp", (JsonRpcMessage message, HttpContext context) =>
        {
            if (message is not JsonRpcRequest request)
            {
                return Results.Accepted();
            }

            context.Response.Headers.Append("mcp-session-id", "hang-test-session");

            if (request.Method == "initialize")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = "2024-11-05",
                        Capabilities = new() { Tools = new() },
                        ServerInfo = new Implementation { Name = "hang-test", Version = "0.0.1" },
                    }, McpJsonUtilities.DefaultOptions)
                });
            }

            if (request.Method == "tools/list")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult
                    {
                        Tools = [echoTool.ProtocolTool]
                    }, McpJsonUtilities.DefaultOptions),
                });
            }

            return Results.Accepted();
        });

        // GET handler that keeps the SSE stream open indefinitely (like a real MCP server)
        _app.MapGet("/mcp", async context =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            getRequestReceived.TrySetResult();
            await context.Response.Body.FlushAsync(TestContext.Current.CancellationToken);

            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        });

        await _app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        await using (var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Single(tools);

            // Wait for the GET SSE stream to be established on the server
            await getRequestReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

            // Dispose should not hang even though the GET stream is actively open
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static async Task CallEchoAndValidateAsync(McpClientTool echoTool)
    {
        var response = await echoTool.CallAsync(new Dictionary<string, object?>() { ["message"] = "Hello world!" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        var content = Assert.Single(response.Content);
        Assert.Equal("Hello world!", Assert.IsType<TextContentBlock>(content).Text);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    [McpServerTool(Name = "echo")]
    private static string Echo(string message)
    {
        return message;
    }

    private sealed class ResumeTestServer
    {
        private static readonly Tool ResumeTool = new()
        {
            Name = "resume-echo",
            Description = "Echoes the provided message.",
        };

        private readonly string _expectedSessionId;
        private readonly TaskCompletionSource<string> _getStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _postSessionIds = [];
        private readonly List<string> _deleteSessionIds = [];

        public ResumeTestServer(string expectedSessionId)
        {
            _expectedSessionId = expectedSessionId;
        }

        public Task<string> GetStarted => _getStarted.Task;
        public IReadOnlyList<string> PostSessionIds => _postSessionIds;
        public IReadOnlyList<string> DeleteSessionIds => _deleteSessionIds;

        public void MapEndpoints(WebApplication app)
        {
            app.MapGet("/mcp", HandleGetAsync);
            app.MapPost("/mcp", HandlePostAsync);
            app.MapDelete("/mcp", HandleDeleteAsync);
        }

        private async Task HandleGetAsync(HttpContext context)
        {
            var sessionId = context.Request.Headers["mcp-session-id"].ToString();
            if (!string.Equals(sessionId, _expectedSessionId, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.Headers.ContentType = "text/event-stream";
            _getStarted.TrySetResult(sessionId);
            await context.Response.Body.FlushAsync();

            try
            {
                await Task.Delay(Timeout.Infinite, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task HandlePostAsync(HttpContext context)
        {
            var sessionId = context.Request.Headers["mcp-session-id"].ToString();
            _postSessionIds.Add(sessionId);

            if (!string.Equals(sessionId, _expectedSessionId, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var request = await context.Request.ReadFromJsonAsync(GetJsonTypeInfo<JsonRpcRequest>(), context.RequestAborted);
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (request.Method == RequestMethods.ToolsList)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult
                    {
                        Tools = [ResumeTool],
                    }, McpJsonUtilities.DefaultOptions),
                };

                await context.Response.WriteAsJsonAsync(response, cancellationToken: context.RequestAborted);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        }

        private Task HandleDeleteAsync(HttpContext context)
        {
            _deleteSessionIds.Add(context.Request.Headers["mcp-session-id"].ToString());
            return Task.CompletedTask;
        }
    }
}
