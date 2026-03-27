using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Net;
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
    public async Task SseEndpoints_AreDisabledByDefault_InStatefulMode()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            // Stateful mode, but SSE not explicitly enabled.
            options.Stateless = false;
        });
        await using var app = Builder.Build();

        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var sseResponse = await HttpClient.GetAsync("/sse", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, sseResponse.StatusCode);

        using var messageResponse = await HttpClient.PostAsync("/message", new StringContent(""), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, messageResponse.StatusCode);
    }

    [Fact]
    public async Task SseEndpoints_ThrowOnMapMcp_InStatelessMode_WithEnableLegacySse()
    {
        Builder.Services.AddMcpServer().WithHttpTransport(options =>
        {
            options.Stateless = true;
            options.EnableLegacySse = true;
        });
        await using var app = Builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapMcp());
        Assert.Contains("stateless", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EnableLegacySse", ex.Message);
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
        }).WithHttpTransport(options => { ConfigureStateless(options); options.EnableLegacySse = true; });
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
        }).WithHttpTransport(options => { ConfigureStateless(options); options.EnableLegacySse = true; });
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

    [Fact]
    public async Task Client_CanReconnect_AfterSessionExpiry()
    {
        Assert.SkipWhen(Stateless, "Sessions don't exist in stateless mode.");

        string? expiredSessionId = null;

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<ClaimsPrincipalTools>();

        await using var app = Builder.Build();

        // Middleware that returns 404 for the expired session, simulating server-side session expiry.
        app.Use(next =>
        {
            return async context =>
            {
                if (expiredSessionId is not null &&
                    context.Request.Headers["Mcp-Session-Id"].ToString() == expiredSessionId)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next(context);
            };
        });

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        // Connect the first client and verify it works.
        var client1 = await ConnectAsync();
        var originalSessionId = client1.SessionId;
        Assert.NotNull(originalSessionId);

        var tools = await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);

        // Simulate session expiry by having the middleware reject the original session.
        expiredSessionId = originalSessionId;

        // The next request should fail.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client1.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Completion should resolve with a 404 status code.
        var details = await client1.Completion.WaitAsync(TestContext.Current.CancellationToken);
        var httpDetails = Assert.IsType<HttpClientCompletionDetails>(details);
        Assert.Equal(HttpStatusCode.NotFound, httpDetails.HttpStatusCode);

        await client1.DisposeAsync();

        // Reconnect with a brand-new session.
        await using var client2 = await ConnectAsync();
        Assert.NotNull(client2.SessionId);
        Assert.NotEqual(originalSessionId, client2.SessionId);

        // The new session works normally.
        tools = await client2.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(tools);
    }

    [Fact]
    public async Task EndpointFilter_CanReadSessionId_BeforeAndAfterHandler()
    {
        var capturedSessionIds = new ConcurrentBag<(string? BeforeNext, string? AfterNext, string Method)>();

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools<EchoHttpContextUserTools>();

        await using var app = Builder.Build();

        // This is the pattern documented in sessions.md — verify it actually works.
        app.MapMcp().AddEndpointFilter(async (context, next) =>
        {
            var httpContext = context.HttpContext;

            // Read from request headers — available on all non-initialize requests in stateful mode.
            var beforeSessionId = httpContext.Request.Headers["Mcp-Session-Id"].FirstOrDefault();

            var result = await next(context);

            // After the handler, check response headers.
            var afterSessionId = httpContext.Response.Headers["Mcp-Session-Id"].FirstOrDefault();

            capturedSessionIds.Add((beforeSessionId, afterSessionId, httpContext.Request.Method));
            return result;
        });

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // The filter ran for at least the initialize, initialized notification, and list_tools POSTs.
        Assert.True(capturedSessionIds.Count >= 3);

        if (Stateless)
        {
            // Stateless mode: no session IDs anywhere.
            Assert.All(capturedSessionIds, c =>
            {
                Assert.Null(c.BeforeNext);
                Assert.Null(c.AfterNext);
            });
        }
        else
        {
            // Stateful mode: response header is set on every POST and GET response.
            var postAndGetCaptures = capturedSessionIds.Where(c => c.Method is "POST" or "GET");
            Assert.All(postAndGetCaptures, c =>
            {
                Assert.Equal(client.SessionId, c.AfterNext);
            });

            // At least one POST should have the session ID in the request header too
            // (the initialized notification or list_tools — but not the initial initialize request).
            Assert.Contains(capturedSessionIds, c => c.BeforeNext == client.SessionId);
        }
    }
}
