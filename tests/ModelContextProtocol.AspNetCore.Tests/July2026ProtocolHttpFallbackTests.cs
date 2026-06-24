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
/// Regression tests for the 2026-07-28-to-legacy fallback path over Streamable HTTP. These
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
///     any other JSON-RPC error code in a <c>400</c> body (e.g. <c>-32600</c> from a legacy server
///     that doesn't understand the 2026-07-28 <c>_meta</c> envelope) threw <see cref="HttpRequestException"/>
///     and bypassed the connect-time fallback logic. Per spec PR #2844, the fallback must trigger
///     on ANY non-modern JSON-RPC error in a <c>400</c> body.
///   </item>
///   <item>
///     <see cref="AutoDetectingClientSessionTransport"/> treated any non-2xx HTTP response as a
///     signal to abandon the Streamable HTTP transport and fall back to SSE. That masked
///     application-level errors (including the three modern codes) because the SSE GET would
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
    /// initial <c>server/discover</c>, then performs a normal legacy <c>initialize</c> handshake
    /// when the client falls back.
    /// </summary>
    [Fact]
    public async Task Client_AgainstLegacyHttpServer_FallsBack_To_Initialize_When_400_Contains_JsonRpcError()
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

            // 2026-07-28 probe: simulate a legacy server that rejects the unknown protocol version with
            // a -32600 envelope (matches Python's wire shape verified in cross-SDK testing).
            if (request.Method == RequestMethods.ServerDiscover)
            {
                await WriteJsonRpcErrorAsync(context, HttpStatusCode.BadRequest, code: -32600, message: "Bad Request: Unsupported protocol version: 2026-07-28");
                return;
            }

            // Legacy initialize: respond with the highest version the legacy server speaks.
            if (request.Method == RequestMethods.Initialize)
            {
                var response = new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = "2025-06-18",
                        Capabilities = new() { Tools = new() },
                        ServerInfo = new Implementation { Name = "legacy", Version = "1.0" },
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

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal("2025-06-18", client.NegotiatedProtocolVersion);

        // Sanity: subsequent traffic still works post-fallback.
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        Assert.Empty(tools);
    }

    /// <summary>
    /// Mimics vanilla Go: returns <c>400</c> + JSON-RPC <c>-32022</c> with
    /// <c>data.supported[]</c> on a 2026-07-28 probe so the client retries legacy
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
                    Supported = new List<string> { "2025-11-25" },
                    Requested = "2026-07-28",
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
                        ProtocolVersion = "2025-11-25",
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

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal("2025-11-25", client.NegotiatedProtocolVersion);
    }

    /// <summary>
    /// A 400 with a JSON-RPC <c>-32020 HeaderMismatch</c> envelope must be surfaced to the
    /// caller (no legacy fallback). Falling back wouldn't fix a malformed envelope.
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
                ProtocolVersion = McpHttpHeaders.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.Equal(McpErrorCode.HeaderMismatch, exception.ErrorCode);
        Assert.False(initializeReceived);
    }
}
