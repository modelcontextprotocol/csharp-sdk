using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpStreamableHttpTests(ITestOutputHelper outputHelper) : MapMcpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => false;

    [Theory]
    [InlineData("/a", "/a")]
    [InlineData("/a", "/a/")]
    [InlineData("/a/", "/a/")]
    [InlineData("/a/", "/a")]
    [InlineData("/a/b", "/a/b")]
    public async Task CanConnect_WithMcpClient_AfterCustomizingRoute(string routePattern, string requestPath)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "TestCustomRouteServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp(routePattern);

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(requestPath);

        Assert.Equal("TestCustomRouteServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task StreamableHttpMode_Works_WithRootEndpoint()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "StreamableHttpTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/", new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.AutoDetect
        });

        Assert.Equal("StreamableHttpTestServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task AutoDetectMode_Works_WithRootEndpoint()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "AutoDetectTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/", new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.AutoDetect
        });

        Assert.Equal("AutoDetectTestServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task AutoDetectMode_Works_WithSseEndpoint()
    {
        Assert.SkipWhen(Stateless, "SSE endpoint is disabled in stateless mode.");

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "AutoDetectSseTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync("/sse", new()
        {
            Endpoint = new("http://localhost:5000/sse"),
            TransportMode = HttpTransportMode.AutoDetect
        });

        Assert.Equal("AutoDetectSseTestServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task SseMode_Works_WithSseEndpoint()
    {
        Assert.SkipWhen(Stateless, "SSE endpoint is disabled in stateless mode.");

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "SseTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(ConfigureStateless);
        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(transportOptions: new()
        {
            Endpoint = new("http://localhost:5000/sse"),
            TransportMode = HttpTransportMode.Sse
        });

        Assert.Equal("SseTestServer", mcpClient.ServerInfo.Name);
    }

    [Fact]
    public async Task StreamableHttpClient_SendsMcpProtocolVersionHeader_AfterInitialization()
    {
        var protocolVersionHeaderValues = new ConcurrentQueue<string?>();

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                if (!StringValues.IsNullOrEmpty(context.Request.Headers["mcp-protocol-version"]))
                {
                    protocolVersionHeaderValues.Enqueue(context.Request.Headers["mcp-protocol-version"]);
                }

                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync(clientOptions: new()
        {
            ProtocolVersion = "2025-06-18",
        });

        Assert.Equal("2025-06-18", mcpClient.NegotiatedProtocolVersion);
        await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        await mcpClient.DisposeAsync();

        // The GET request might not have started in time, and the DELETE request won't be sent in
        // Stateless mode due to the lack of an Mcp-Session-Id, but the header should be included in the
        // initialized notification and the tools/list call at a minimum.
        Assert.True(protocolVersionHeaderValues.Count > 1);
        Assert.All(protocolVersionHeaderValues, v => Assert.Equal("2025-06-18", v));
    }

    [Fact]
    public async Task CanResumeSessionWithMapMcpAndRunSessionHandler()
    {
        Assert.SkipWhen(Stateless, "Session resumption relies on server-side session tracking.");

        var runSessionCount = 0;
        var serverTcs = new TaskCompletionSource<McpServer>(TaskCreationOptions.RunContinuationsAsynchronously);

        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "ResumeServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(opts =>
        {
            ConfigureStateless(opts);
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
            opts.RunSessionHandler = async (context, server, cancellationToken) =>
            {
                Interlocked.Increment(ref runSessionCount);
                serverTcs.TrySetResult(server);
                await server.RunAsync(cancellationToken);
            };
#pragma warning restore MCPEXP002
        }).WithTools<EchoHttpContextUserTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        ServerCapabilities? serverCapabilities = null;
        Implementation? serverInfo = null;
        string? serverInstructions = null;
        string? negotiatedProtocolVersion = null;
        string? resumedSessionId = null;

        await using var initialTransport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        await using (var initialClient = await McpClient.CreateAsync(initialTransport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken))
        {
            resumedSessionId = initialClient.SessionId ?? throw new InvalidOperationException("SessionId not negotiated.");
            serverCapabilities = initialClient.ServerCapabilities;
            serverInfo = initialClient.ServerInfo;
            serverInstructions = initialClient.ServerInstructions;
            negotiatedProtocolVersion = initialClient.NegotiatedProtocolVersion;

            await initialClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.NotNull(serverCapabilities);
        Assert.NotNull(serverInfo);
        Assert.False(string.IsNullOrEmpty(resumedSessionId));

        await serverTcs.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        await using var resumeTransport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            KnownSessionId = resumedSessionId!,
        }, HttpClient, LoggerFactory);

        var resumeOptions = new ResumeClientSessionOptions
        {
            ServerCapabilities = serverCapabilities!,
            ServerInfo = serverInfo!,
            ServerInstructions = serverInstructions,
            NegotiatedProtocolVersion = negotiatedProtocolVersion,
        };

        await using (var resumedClient = await McpClient.ResumeSessionAsync(
            resumeTransport,
            resumeOptions,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            var tools = await resumedClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEmpty(tools);

            Assert.Equal(serverInstructions, resumedClient.ServerInstructions);
            Assert.Equal(negotiatedProtocolVersion, resumedClient.NegotiatedProtocolVersion);
        }

        Assert.Equal(1, runSessionCount);
    }

    [Fact]
    public async Task EnablePollingAsync_ThrowsInvalidOperationException_InStatelessMode()
    {
        Assert.SkipUnless(Stateless, "This test only applies to stateless mode.");

        InvalidOperationException? capturedException = null;
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            try
            {
                await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException ex)
            {
                capturedException = ex;
            }

            return "Complete";
        }, options: new() { Name = "polling_tool" });

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools([pollingTool]);

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        await mcpClient.CallToolAsync("polling_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedException);
        Assert.Contains("stateless", capturedException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnablePollingAsync_ThrowsInvalidOperationException_WhenNoEventStreamStoreConfigured()
    {
        Assert.SkipWhen(Stateless, "This test only applies to stateful mode without an event stream store.");

        InvalidOperationException? capturedException = null;
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            try
            {
                await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException ex)
            {
                capturedException = ex;
            }

            return "Complete";
        }, options: new() { Name = "polling_tool" });

        // Configure without EventStreamStore
        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools([pollingTool]);

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        await mcpClient.CallToolAsync("polling_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedException);
        Assert.Contains("event stream store", capturedException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdditionalHeaders_AreSent_InPostAndDeleteRequests()
    {
        Assert.SkipWhen(Stateless, "DELETE requests are not sent in stateless mode due to lack of session ID.");

        bool wasPostRequest = false;
        bool wasDeleteRequest = false;

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        await using var app = Builder.Build();

        app.Use(next =>
        {
            return async context =>
            {
                Assert.Equal("Bearer testToken", context.Request.Headers["Authorize"]);
                if (context.Request.Method == HttpMethods.Post)
                {
                    wasPostRequest = true;
                }
                else if (context.Request.Method == HttpMethods.Delete)
                {
                    wasDeleteRequest = true;
                }
                await next(context);
            };
        });

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new("http://localhost:5000/"),
            Name = "In-memory Streamable HTTP Client",
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorize"] = "Bearer testToken"
            },
        };

        await using var mcpClient = await ConnectAsync(transportOptions: transportOptions);

        // Do a tool call to ensure there's more than just the initialize request
        await mcpClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Dispose the client to trigger the DELETE request
        await mcpClient.DisposeAsync();

        Assert.True(wasPostRequest, "POST request was not made");
        Assert.True(wasDeleteRequest, "DELETE request was not made");
    }

    [Fact]
    public async Task DisposeAsync_DoesNotHang_WhenOwnsSessionIsFalse()
    {
        Assert.SkipWhen(Stateless, "Stateless mode doesn't support session management.");

        var getResponseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();

        // Track when the GET SSE response starts being written, which indicates
        // the server's HandleGetRequestAsync has fully initialized the SSE writer.
        app.Use(next =>
        {
            return async context =>
            {
                if (context.Request.Method == HttpMethods.Get)
                {
                    context.Response.OnStarting(() =>
                    {
                        getResponseStarted.TrySetResult();
                        return Task.CompletedTask;
                    });
                }
                await next(context);
            };
        });

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Call a tool to ensure the session is fully established
        var result = await client.CallToolAsync(
            "echo_claims_principal",
            new Dictionary<string, object?>() { ["message"] = "Hello!" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        // Wait for the GET SSE stream to be fully established on the server
        await getResponseStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // This should not hang. The issue reports that DisposeAsync hangs indefinitely
        // when OwnsSession is false. Use a timeout to detect the hang.
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotHang_WhenOwnsSessionIsFalse_WithUnsolicitedMessages()
    {
        Assert.SkipWhen(Stateless, "Stateless mode doesn't support session management.");

        var getResponseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTcs = new TaskCompletionSource<McpServer>(TaskCreationOptions.RunContinuationsAsynchronously);

        Builder.Services.AddMcpServer().WithHttpTransport(opts =>
        {
            ConfigureStateless(opts);
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental
            opts.RunSessionHandler = async (context, server, cancellationToken) =>
            {
                serverTcs.TrySetResult(server);
                await server.RunAsync(cancellationToken);
            };
#pragma warning restore MCPEXP002
        }).WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();

        // Track when the GET SSE response starts being written, which indicates
        // the server's HandleGetRequestAsync has fully initialized the SSE writer.
        app.Use(next =>
        {
            return async context =>
            {
                if (context.Request.Method == HttpMethods.Get)
                {
                    context.Response.OnStarting(() =>
                    {
                        getResponseStarted.TrySetResult();
                        return Task.CompletedTask;
                    });
                }
                await next(context);
            };
        });

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "echo_claims_principal",
            new Dictionary<string, object?>() { ["message"] = "Hello!" },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(result);

        // Wait for the GET SSE stream to be fully established on the server
        await getResponseStarted.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Register a handler on the client to detect when the notification is received
        var notificationReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handlerRegistration = client.RegisterNotificationHandler("notifications/tools/list_changed", (notification, ct) =>
        {
            notificationReceived.TrySetResult();
            return default;
        });

        // Get the server instance and send an unsolicited notification by modifying tools
        var server = await serverTcs.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        await server.SendNotificationAsync("notifications/tools/list_changed", TestContext.Current.CancellationToken);

        // Wait for the client to actually receive the notification
        await notificationReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        // Dispose should still not hang
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }
}
