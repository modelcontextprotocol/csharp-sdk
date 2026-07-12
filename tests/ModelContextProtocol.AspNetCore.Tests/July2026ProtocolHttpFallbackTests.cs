using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Regression tests for the fallback from a 2026-07-28 per-request-metadata probe to the initialize
/// handshake over Streamable HTTP. These
/// hand-craft minimal HTTP servers that mimic real-world peer behavior (e.g. Python's
/// <c>simple-streamablehttp-stateless</c> returns a JSON-RPC error envelope in a <c>400</c> body
/// on a 2026-07-28 probe; vanilla Go does the same on <c>POST /</c>) so the client's HTTP-fallback
/// logic can be exercised in isolation without the cross-SDK harness.
/// </summary>
/// <remarks>
/// <para>
/// Two latent bugs were discovered during cross-SDK testing and fixed by the SEP-2575 / SEP-2567
/// branch:
/// </para>
/// <list type="number">
///   <item>
///     <see cref="StreamableHttpClientSessionTransport"/> only surfaced the three error codes
///     introduced by the 2026-07-28 revision (<c>-32022</c>, <c>-32021</c>, <c>-32020</c>) as <see cref="McpProtocolException"/>;
///     any other JSON-RPC error code in a <c>400</c> body (e.g. <c>-32600</c> from an initialize-handshake server
///     that doesn't understand the 2026-07-28 <c>_meta</c> envelope) threw <see cref="HttpRequestException"/>
///     and bypassed the connect-time fallback logic. Per spec PR #2844, the fallback must trigger
///     on ANY non-SEP-2575 JSON-RPC error in a <c>400</c> body.
///   </item>
///   <item>
///     <see cref="AutoDetectingClientSessionTransport"/> treated any non-2xx HTTP response as a
///     signal to abandon the Streamable HTTP transport and fall back to SSE. That masked
///     application-level errors (including the three SEP-2575/SEP-2567 codes) because the SSE GET would
///     either fail with "session id required" or succeed against a different endpoint and lose
///     the actual signal.
///   </item>
/// </list>
/// </remarks>
public class July2026ProtocolHttpFallbackTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    private async Task StartServerAsync(RequestDelegate handler)
    {
        Builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(McpJsonUtilities.DefaultOptions.TypeInfoResolver!);
        });

        _app = Builder.Build();
        _app.MapPost("/mcp", handler);
        await _app.StartAsync(TestContext.Current.CancellationToken);
    }

    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static async Task WriteJsonRpcErrorAsync(HttpContext context, HttpStatusCode statusCode, int code, string message)
    {
        var rpcError = new JsonRpcError
        {
            Id = default,
            Error = new JsonRpcErrorDetail { Code = code, Message = message },
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(rpcError, GetJsonTypeInfo<JsonRpcMessage>()), context.RequestAborted);
    }

    /// <summary>
    /// Mimics Python's <c>simple-streamablehttp-stateless</c> on a 2026-07-28 probe: returns
    /// <c>400</c> + JSON-RPC <c>-32600</c> ("Bad Request: Unsupported protocol version") for the
    /// initial <c>server/discover</c>, then performs a normal <c>initialize</c> handshake
    /// when the client falls back.
    /// </summary>
    [Fact]
    public async Task Client_AgainstInitializeHandshakeHttpServer_FallsBack_To_Initialize_When_400_Contains_JsonRpcError()
    {
        var ct = TestContext.Current.CancellationToken;

        await StartServerAsync(async context =>
        {
            var message = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                GetJsonTypeInfo<JsonRpcMessage>(),
                ct);

            if (message is not JsonRpcRequest request)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            // 2026-07-28 probe: simulate an initialize-handshake server that rejects the unknown protocol version with
            // a -32600 envelope (matches Python's wire shape verified in cross-SDK testing).
            if (request.Method == RequestMethods.ServerDiscover)
            {
                await WriteJsonRpcErrorAsync(context, HttpStatusCode.BadRequest, code: -32600, message: "Bad Request: Unsupported protocol version: 2026-07-28");
                return;
            }

            // Initialize handshake: respond with the highest version this server speaks.
            if (request.Method == RequestMethods.Initialize)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = McpProtocolVersions.June2025ProtocolVersion,
                        Capabilities = new() { Tools = new() },
                        ServerInfo = new Implementation { Name = "initialize-handshake", Version = "1.0" },
                    }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            if (request.Method == RequestMethods.ToolsList)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [] }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        // Default AutoDetect transport — exercises BOTH fixes (AutoDetect adopting StreamableHttp
        // on JSON-RPC-error 400, and SendMessageAsync surfacing -32600 as McpProtocolException).
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
        }, HttpClient, LoggerFactory);

        // Default options prefer 2026-07-28 but allow automatic fallback to an initialize-handshake server.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal(McpProtocolVersions.June2025ProtocolVersion, client.NegotiatedProtocolVersion);

        // Sanity: subsequent traffic still works post-fallback.
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        Assert.Empty(tools);
    }

    /// <summary>
    /// Mimics vanilla Go: returns <c>400</c> + JSON-RPC <c>-32022</c> with
    /// <c>data.supported[]</c> on a 2026-07-28 probe so the client retries
    /// <c>initialize</c> with one of the advertised versions.
    /// </summary>
    [Fact]
    public async Task Client_OnUnsupportedProtocolVersion_AdoptsStreamableHttp_NoSseFallback()
    {
        var ct = TestContext.Current.CancellationToken;

        await StartServerAsync(async context =>
        {
            var message = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                GetJsonTypeInfo<JsonRpcMessage>(),
                ct);

            if (message is not JsonRpcRequest request)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            if (request.Method == RequestMethods.ServerDiscover)
            {
                // -32022 with the spec-shaped data: client should retry with one of supported[].
                // Use the typed payload type so the source-generated serializer can handle it.
                var data = JsonSerializer.SerializeToNode(new UnsupportedProtocolVersionErrorData
                {
                    Supported = new List<string> { McpProtocolVersions.November2025ProtocolVersion },
                    Requested = McpProtocolVersions.July2026ProtocolVersion,
                }, GetJsonTypeInfo<UnsupportedProtocolVersionErrorData>());

                var rpcError = new JsonRpcError
                {
                    Id = request.Id,
                    Error = new JsonRpcErrorDetail
                    {
                        Code = (int)McpErrorCode.UnsupportedProtocolVersion,
                        Message = "Unsupported protocol version",
                        Data = data,
                    },
                };

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(rpcError, GetJsonTypeInfo<JsonRpcMessage>()), ct);
                return;
            }

            if (request.Method == RequestMethods.Initialize)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                        Capabilities = new() { Tools = new() },
                        ServerInfo = new Implementation { Name = "go-shaped", Version = "1.0" },
                    }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
        }, HttpClient, LoggerFactory);

        // Default options prefer 2026-07-28 but allow automatic fallback to an initialize-handshake server.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    /// <summary>
    /// A 400 with a JSON-RPC <c>-32020 HeaderMismatch</c> envelope must be surfaced to the
    /// caller (no initialize fallback). Falling back wouldn't fix a malformed envelope.
    /// </summary>
    [Fact]
    public async Task Client_OnHeaderMismatch_400_Surfaces_McpProtocolException_NoFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        bool initializeReceived = false;

        await StartServerAsync(async context =>
        {
            var message = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                GetJsonTypeInfo<JsonRpcMessage>(),
                ct);

            if (message is JsonRpcRequest { Method: RequestMethods.Initialize })
            {
                initializeReceived = true;
            }

            if (message is JsonRpcRequest { Method: RequestMethods.ServerDiscover })
            {
                await WriteJsonRpcErrorAsync(context, HttpStatusCode.BadRequest,
                    code: (int)McpErrorCode.HeaderMismatch,
                    message: "Header mismatch: MCP-Protocol-Version did not match body _meta");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
        }, HttpClient, LoggerFactory);

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.Equal(McpErrorCode.HeaderMismatch, exception.ErrorCode);
        Assert.False(initializeReceived);
    }

    [Fact]
    public async Task Client_OnPerRequestMetadataResponseWithMcpSessionId_IgnoresSessionState()
    {
        var ct = TestContext.Current.CancellationToken;
        string? toolsListSessionId = null;

        await StartServerAsync(async context =>
        {
            var message = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                GetJsonTypeInfo<JsonRpcMessage>(),
                ct);

            if (message is JsonRpcRequest { Method: RequestMethods.ServerDiscover } request)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new DiscoverResult
                    {
                        SupportedVersions = [McpProtocolVersions.July2026ProtocolVersion],
                        Capabilities = new ServerCapabilities(),
                        ServerInfo = new Implementation { Name = "bad-per-request-metadata-server", Version = "1.0" },
                        TimeToLive = TimeSpan.Zero,
                        CacheScope = CacheScope.Private,
                    }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.Headers[McpHttpHeaders.SessionId] = "unexpected-session";
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            if (message is JsonRpcRequest { Method: RequestMethods.ToolsList } toolsListRequest)
            {
                toolsListSessionId = context.Request.Headers[McpHttpHeaders.SessionId].ToString();

                var response = new JsonRpcResponse
                {
                    Id = toolsListRequest.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult { Tools = [] }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        await client.ListToolsAsync(cancellationToken: ct);

        Assert.Null(client.SessionId);
        Assert.Equal("", toolsListSessionId);
    }

    [Fact]
    public async Task Client_WithKnownSessionId_DoesNotEchoIt_OnPerRequestMetadataRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        string? discoverSessionId = null;

        await StartServerAsync(async context =>
        {
            var message = await JsonSerializer.DeserializeAsync(
                context.Request.Body,
                GetJsonTypeInfo<JsonRpcMessage>(),
                ct);

            if (message is JsonRpcRequest { Method: RequestMethods.ServerDiscover } request)
            {
                discoverSessionId = context.Request.Headers[McpHttpHeaders.SessionId].ToString();

                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new DiscoverResult
                    {
                        SupportedVersions = [McpProtocolVersions.July2026ProtocolVersion],
                        Capabilities = new ServerCapabilities(),
                        ServerInfo = new Implementation { Name = "per-request-metadata-server", Version = "1.0" },
                        TimeToLive = TimeSpan.Zero,
                        CacheScope = CacheScope.Private,
                    }, McpJsonUtilities.DefaultOptions),
                };

                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, GetJsonTypeInfo<JsonRpcMessage>(), ct);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            KnownSessionId = "legacy-session",
            OwnsSession = false,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal("", discoverSessionId);
    }
}
