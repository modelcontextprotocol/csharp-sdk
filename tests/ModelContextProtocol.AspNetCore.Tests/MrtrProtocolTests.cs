using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Protocol-level tests for Multi Round-Trip Requests (MRTR) over the 2026-07-28 protocol revision.
/// Under that revision (SEP-2575 + SEP-2567) Streamable HTTP no longer supports sessions, so these tests
/// drive the default <see cref="ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless"/> server with raw
/// JSON-RPC requests (no <c>initialize</c>, no <c>Mcp-Session-Id</c>) and verify the explicit
/// MRTR <see cref="InputRequiredResult"/> structure, retry with inputResponses, and error handling.
/// Stateful-session MRTR behaviors (implicit handler suspension, disposal cancellation) are covered
/// over stdio by <c>MrtrHandlerLifecycleTests</c>, and unknown-session rejection by
/// <c>StreamableHttpServerConformanceTests.PostRequest_IsNotFound_WithUnrecognizedSessionId</c>.
/// </summary>
public class MrtrProtocolTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{

    private WebApplication? _app;

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(MrtrProtocolTests),
                Version = "1",
            };
        }).WithTools([
            McpServerTool.Create(
                static string (McpServer _) => throw new McpProtocolException("Tool validation failed", McpErrorCode.InvalidParams),
                new McpServerToolCreateOptions
                {
                    Name = "throwing-tool",
                    Description = "A tool that throws immediately"
                }),
            McpServerTool.Create(
                static CallToolResult (RequestContext<CallToolRequestParams> context) =>
                {
                    // Mirrors ConformanceServer.Tools.IncompleteResultTools.ToolWithTamperedState:
                    // R1 (no requestState) issues a requestState; R2 with a tampered requestState
                    // surfaces a JSON-RPC error rather than a complete result or a re-prompt.
                    if (context.Params!.RequestState is { } state)
                    {
                        if (state != "valid-request-state-token")
                        {
                            throw new McpProtocolException(
                                "requestState failed integrity verification.", McpErrorCode.InvalidParams);
                        }

                        return new CallToolResult { Content = [new TextContentBlock { Text = "state-ok" }] };
                    }

                    throw new InputRequiredException(
                        new Dictionary<string, InputRequest>
                        {
                            ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please confirm",
                                RequestedSchema = new ElicitRequestParams.RequestSchema
                                {
                                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                                    {
                                        ["ok"] = new ElicitRequestParams.BooleanSchema(),
                                    },
                                    Required = ["ok"],
                                },
                            }),
                        },
                        requestState: "valid-request-state-token");
                },
                new McpServerToolCreateOptions
                {
                    Name = "tampered-state-tool",
                    Description = "Rejects a tampered requestState with a JSON-RPC error"
                }),
            McpServerTool.Create(
                static CallToolResult (RequestContext<CallToolRequestParams> context) =>
                {
                    // Mirrors ConformanceServer.Tools.IncompleteResultTools.ToolWithCapabilityCheck:
                    // emit inputRequests only for capabilities declared on the per-request _meta envelope.
                    var caps = context.JsonRpcRequest.Context?.ClientCapabilities;
                    var inputRequests = new Dictionary<string, InputRequest>();

                    if (caps?.Sampling is not null)
                    {
                        inputRequests["capital_question"] = InputRequest.ForSampling(new CreateMessageRequestParams
                        {
                            Messages =
                            [
                                new SamplingMessage
                                {
                                    Role = Role.User,
                                    Content = [new TextContentBlock { Text = "What is the capital of France?" }],
                                },
                            ],
                            MaxTokens = 100,
                        });
                    }

                    if (caps?.Elicitation is not null)
                    {
                        inputRequests["user_name"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "What is your name?",
                            RequestedSchema = new ElicitRequestParams.RequestSchema
                            {
                                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                                {
                                    ["name"] = new ElicitRequestParams.StringSchema(),
                                },
                                Required = ["name"],
                            },
                        });
                    }

                    if (inputRequests.Count == 0)
                    {
                        return new CallToolResult { Content = [new TextContentBlock { Text = "no-caps" }] };
                    }

                    throw new InputRequiredException(inputRequests);
                },
                new McpServerToolCreateOptions
                {
                    Name = "capability-check-tool",
                    Description = "Gates inputRequests on the per-request _meta clientCapabilities envelope"
                }),
        ]).WithHttpTransport();

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        // Drive the server with raw requests: every request carries the 2026-07-28 protocol
        // MCP-Protocol-Version header and (via PostJsonRpcAsync) the SEP-2243 Mcp-Method/Mcp-Name
        // headers. No initialize handshake and no Mcp-Session-Id.
        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    [Fact]
    public async Task ToolThatThrows_ReturnsJsonRpcError_NotIncompleteResult()
    {
        await StartAsync();

        var response = await PostJsonRpcAsync(CallTool("throwing-tool"));

        // Should be a JSON-RPC error, not an InputRequiredResult
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sseData = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(sseData, McpJsonUtilities.DefaultOptions);
        var error = Assert.IsType<JsonRpcError>(message);
        Assert.Equal((int)McpErrorCode.InvalidParams, error.Error.Code);
        Assert.Contains("Tool validation failed", error.Error.Message);
    }

    [Fact]
    public async Task TamperedRequestState_ReturnsJsonRpcError()
    {
        await StartAsync();

        // Round 1: no requestState -> InputRequiredResult carrying the issued requestState.
        using var r1 = await PostJsonRpcAsync(CallTool("tampered-state-tool"));
        var r1Response = await AssertSingleSseResponseAsync(r1);
        var r1Result = Assert.IsType<JsonObject>(r1Response.Result);
        Assert.Equal("input_required", r1Result["resultType"]?.GetValue<string>());

        var requestState = r1Result["requestState"]!.GetValue<string>();
        var inputKey = r1Result["inputRequests"]!.AsObject().First().Key;

        // Round 2: tamper the requestState the way the conformance harness does and retry.
        // The tool MUST reject it with a JSON-RPC error (not a complete result, not a re-prompt).
        var inputResponse = InputResponse.FromElicitResult(new ElicitResult { Action = "accept" });
        var retryParams = new JsonObject
        {
            ["name"] = "tampered-state-tool",
            ["arguments"] = new JsonObject(),
            ["requestState"] = requestState + "-TAMPERED",
            ["inputResponses"] = new JsonObject
            {
                [inputKey] = JsonSerializer.SerializeToNode(inputResponse, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(InputResponse)))
            },
        };

        using var r2 = await PostJsonRpcAsync(Request("tools/call", retryParams.ToJsonString()));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var sseData = Assert.Single(await ReadSseAsync(r2.Content).ToListAsync(TestContext.Current.CancellationToken));
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(sseData, McpJsonUtilities.DefaultOptions);
        var error = Assert.IsType<JsonRpcError>(message);
        Assert.Equal((int)McpErrorCode.InvalidParams, error.Error.Code);
    }

    [Fact]
    public async Task CapabilityCheck_OnlyEmitsInputRequestsForDeclaredCapabilities()
    {
        await StartAsync();

        // Per SEP-2575 the client declares capabilities per request in
        // _meta['io.modelcontextprotocol/clientCapabilities']. Declare ONLY sampling: the tool
        // must emit a sampling/createMessage inputRequest but no elicitation/create.
        var callParams = new JsonObject
        {
            ["name"] = "capability-check-tool",
            ["arguments"] = new JsonObject(),
            ["_meta"] = new JsonObject
            {
                ["io.modelcontextprotocol/clientCapabilities"] = new JsonObject
                {
                    ["sampling"] = new JsonObject(),
                },
            },
        };

        using var response = await PostJsonRpcAsync(Request("tools/call", callParams.ToJsonString()));
        var rpcResponse = await AssertSingleSseResponseAsync(response);
        var resultObj = Assert.IsType<JsonObject>(rpcResponse.Result);
        Assert.Equal("input_required", resultObj["resultType"]?.GetValue<string>());

        var inputRequests = resultObj["inputRequests"]!.AsObject();
        Assert.Contains(inputRequests, kvp => kvp.Value!["method"]?.GetValue<string>() == "sampling/createMessage");
        Assert.DoesNotContain(inputRequests, kvp => kvp.Value!["method"]?.GetValue<string>() == "elicitation/create");
    }

    /// <summary>
    /// Regression test for a CI hang where the server-side MRTR backcompat resolver routed its
    /// outgoing <c>roots/list</c> request through the session-level transport, which silently
    /// dropped the message when the client's GET stream had not been established yet. The
    /// outgoing request must instead go through the POST's response stream (the request's
    /// <see cref="ModelContextProtocol.Protocol.JsonRpcMessageContext.RelatedTransport"/>) so it
    /// reaches the client without depending on the GET stream at all.
    ///
    /// This test deliberately never opens a GET stream - it only POSTs the initialize, the
    /// initialized notification, the <c>tools/call</c>, and the <c>roots/list</c> response. If the
    /// server falls back to <c>_transport.SendMessageAsync</c>, the test times out instead of
    /// reading the expected <c>roots/list</c> SSE event off the <c>tools/call</c> POST response.
    /// </summary>
    [Fact]
    public async Task BackcompatResolver_SendsServerRequestOverPostStream_WithoutGetStream()
    {
        // Configure a server that does NOT pin 2026-07-28 so it can negotiate the current
        // initialize-handshake protocol. The backcompat resolver path only runs when the
        // negotiated version is not 2026-07-28.
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(MrtrProtocolTests),
                Version = "1",
            };
        }).WithTools([
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    if (context.Params!.InputResponses is { } responses &&
                        responses.TryGetValue("roots", out var response))
                    {
                        var roots = response.Deserialize(InputResponse.ListRootsResultJsonTypeInfo)?.Roots;
                        return $"roots-ok:{roots?.FirstOrDefault()?.Name}";
                    }

                    throw new InputRequiredException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["roots"] = InputRequest.ForRootsList(new ListRootsRequestParams())
                        },
                        requestState: "roots-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "backcompat-roots-tool",
                    Description = "Throws InputRequiredException so the server's backcompat resolver issues a roots/list",
                }),
        ]).WithHttpTransport(options => options.Stateless = false);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        // Initialize with the current initialize-handshake protocol so the server's backcompat resolver runs.
        var initJson = """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"roots":{}},"clientInfo":{"name":"BackcompatTestClient","version":"1.0.0"}}}
            """;

        string sessionId;
        using (var initResponse = await PostJsonRpcAsync(initJson))
        {
            var initRpcResponse = await AssertSingleSseResponseAsync(initResponse);
            Assert.NotNull(initRpcResponse.Result);
            Assert.Equal("2025-11-25", initRpcResponse.Result["protocolVersion"]?.GetValue<string>());

            sessionId = Assert.Single(initResponse.Headers.GetValues("mcp-session-id"));
        }

        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
        HttpClient.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
        HttpClient.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");

        // Send the initialized notification.
        using (var initializedResponse = await PostJsonRpcAsync(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}"""))
        {
            Assert.True(initializedResponse.IsSuccessStatusCode);
        }

        _lastRequestId = 1;

        // POST the tools/call and start reading the response SSE stream. We deliberately do NOT
        // open a GET stream - the server-to-client roots/list must be delivered on this POST's
        // response. Use HttpCompletionOption.ResponseHeadersRead so the POST returns as soon as
        // the response headers arrive instead of waiting for the SSE stream to close.
        var callRequest = new HttpRequestMessage(HttpMethod.Post, (string?)null)
        {
            Content = JsonContent(CallTool("backcompat-roots-tool", includePerRequestMetadata: false)),
        };
        callRequest.Content.Headers.Add("Mcp-Method", "tools/call");
        callRequest.Content.Headers.Add("Mcp-Name", "backcompat-roots-tool");

        using var callResponse = await HttpClient.SendAsync(
            callRequest,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);
        Assert.Equal("text/event-stream", callResponse.Content.Headers.ContentType?.MediaType);

        var sseEvents = ReadSseAsync(callResponse.Content)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        try
        {
            // First SSE event on this POST should be the server-initiated roots/list request.
            Assert.True(await sseEvents.MoveNextAsync(),
                "Server did not send a roots/list request on the tools/call POST response stream. " +
                "If this hangs/times out, the MRTR backcompat resolver is routing the outgoing request " +
                "through the session-level transport instead of the POST's RelatedTransport.");

            var rootsRequestNode = JsonNode.Parse(sseEvents.Current) as JsonObject;
            Assert.NotNull(rootsRequestNode);
            Assert.Equal("roots/list", rootsRequestNode["method"]?.GetValue<string>());
            var rootsRequestId = rootsRequestNode["id"];
            Assert.NotNull(rootsRequestId);

            // POST the roots/list response on a separate connection. The server's pending
            // RequestRootsAsync await will complete and the backcompat resolver will retry the tool.
            var rootsIdLiteral = rootsRequestId.ToJsonString();
            var rootsResponseJson =
                "{\"jsonrpc\":\"2.0\",\"id\":" + rootsIdLiteral +
                ",\"result\":{\"roots\":[{\"uri\":\"file:///workspace\",\"name\":\"Workspace\"}]}}";
            using (var rootsResponseHttp = await PostJsonRpcAsync(rootsResponseJson))
            {
                Assert.True(rootsResponseHttp.IsSuccessStatusCode);
            }

            // Next SSE event on the original POST should be the final tools/call response.
            Assert.True(await sseEvents.MoveNextAsync(), "Server did not return the final tools/call response.");
            var finalResponse = JsonSerializer.Deserialize(sseEvents.Current, GetJsonTypeInfo<JsonRpcResponse>());
            Assert.NotNull(finalResponse);
            Assert.NotNull(finalResponse.Result);

            var content = finalResponse.Result["content"]?.AsArray();
            Assert.NotNull(content);
            var firstContent = Assert.Single(content);
            Assert.Equal("roots-ok:Workspace", firstContent?["text"]?.GetValue<string>());
        }
        finally
        {
            await sseEvents.DisposeAsync();
        }
    }

    // --- Helpers ---

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");
    private static JsonTypeInfo<T> GetJsonTypeInfo<T>() => (JsonTypeInfo<T>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    private static async IAsyncEnumerable<string> ReadSseAsync(HttpContent responseContent)
    {
        var responseStream = await responseContent.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal("message", sseItem.EventType);
            yield return sseItem.Data;
        }
    }

    private static async Task<JsonRpcResponse> AssertSingleSseResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var sseItem = Assert.Single(await ReadSseAsync(response.Content).ToListAsync(TestContext.Current.CancellationToken));
        var jsonRpcResponse = JsonSerializer.Deserialize(sseItem, GetJsonTypeInfo<JsonRpcResponse>());

        Assert.NotNull(jsonRpcResponse);
        return jsonRpcResponse;
    }

    private Task<HttpResponseMessage> PostJsonRpcAsync(string json)
    {
        var content = JsonContent(json);

        // 2026-07-28 requires Mcp-Method and (for tools/call) Mcp-Name headers per SEP-2243.
        // Parse the body to derive them and attach to this request only.
        var bodyNode = JsonNode.Parse(json);
        if (bodyNode is JsonObject obj)
        {
            if (obj["method"]?.GetValue<string>() is { } method)
            {
                content.Headers.Add("Mcp-Method", method);

                if (obj["params"] is JsonObject paramsObj)
                {
                    string? mcpName = method switch
                    {
                        "tools/call" or "prompts/get" => paramsObj["name"]?.GetValue<string>(),
                        "resources/read" => paramsObj["uri"]?.GetValue<string>(),
                        _ => null,
                    };
                    if (mcpName is not null)
                    {
                        content.Headers.Add("Mcp-Name", mcpName);
                    }
                }
            }
        }

        return HttpClient.PostAsync("", content, TestContext.Current.CancellationToken);
    }

    private long _lastRequestId = 1;

    private string Request(string method, string parameters = "{}", bool includePerRequestMetadata = true)
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        var paramsObj = JsonNode.Parse(parameters) as JsonObject ?? new JsonObject();
        if (includePerRequestMetadata)
        {
            AddJuly2026ProtocolMeta(paramsObj);
        }

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = paramsObj,
        };

        return request.ToJsonString();
    }

    private static void AddJuly2026ProtocolMeta(JsonObject paramsObj)
    {
        if (paramsObj["_meta"] is not JsonObject meta)
        {
            meta = [];
            paramsObj["_meta"] = meta;
        }

        meta[MetaKeys.ProtocolVersion] = McpProtocolVersions.July2026ProtocolVersion;
        meta[MetaKeys.ClientInfo] = new JsonObject
        {
            ["name"] = "MrtrTestClient",
            ["version"] = "1.0",
        };

        if (meta[MetaKeys.ClientCapabilities] is not JsonObject)
        {
            meta[MetaKeys.ClientCapabilities] = new JsonObject();
        }
    }

    private string CallTool(string toolName, string arguments = "{}", bool includePerRequestMetadata = true) =>
        Request("tools/call", $$"""
            {"name":"{{toolName}}","arguments":{{arguments}}}
            """, includePerRequestMetadata);
}
