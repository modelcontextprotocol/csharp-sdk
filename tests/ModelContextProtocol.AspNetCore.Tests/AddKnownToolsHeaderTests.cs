using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests that <see cref="McpClient.AddKnownTools"/> allows sending Mcp-Param-* headers
/// without a prior <see cref="McpClient.ListToolsAsync"/> call.
/// </summary>
public class AddKnownToolsHeaderTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    /// <summary>
    /// Captured headers from tools/call requests, keyed by JSON-RPC request id.
    /// </summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _capturedHeaders = new();

    private async Task StartAsync()
    {
        Builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(McpJsonUtilities.DefaultOptions.TypeInfoResolver!);
        });
        _app = Builder.Build();

        _app.MapPost("/mcp", (JsonRpcMessage message, HttpContext context) =>
        {
            if (message is not JsonRpcRequest request)
            {
                return Results.Accepted();
            }

            if (request.Method == "initialize")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new InitializeResult
                    {
                        ProtocolVersion = "2025-11-25",
                        Capabilities = new() { Tools = new() },
                        ServerInfo = new Implementation { Name = "header-capture-test", Version = "1.0" },
                    }, McpJsonUtilities.DefaultOptions)
                });
            }

            if (request.Method == "tools/call")
            {
                // Capture all Mcp-Param-* headers from the incoming HTTP request
                var paramHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in context.Request.Headers)
                {
                    if (header.Key.StartsWith("Mcp-Param-", StringComparison.OrdinalIgnoreCase))
                    {
                        paramHeaders[header.Key] = header.Value.ToString();
                    }
                }

                _capturedHeaders[request.Id.ToString()!] = paramHeaders;

                var parameters = JsonSerializer.Deserialize(request.Params, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = $"ok" }],
                    }, McpJsonUtilities.DefaultOptions),
                });
            }

            if (request.Method == "tools/list")
            {
                return Results.Json(new JsonRpcResponse
                {
                    Id = request.Id,
                    Result = JsonSerializer.SerializeToNode(new ListToolsResult
                    {
                        Tools = [],
                    }, McpJsonUtilities.DefaultOptions),
                });
            }

            return Results.Accepted();
        });

        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    private static Tool CreateToolWithHeaders()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "region": {
                        "type": "string",
                        "x-mcp-header": "Region"
                    },
                    "priority": {
                        "type": "integer",
                        "x-mcp-header": "Priority"
                    }
                },
                "required": ["region", "priority"]
            }
            """;

        return new Tool
        {
            Name = "my_tool",
            InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone(),
        };
    }

    [Fact]
    public async Task AddKnownTools_ThenCallTool_SendsMcpParamHeaders_WithoutListToolsAsync()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Register the tool WITHOUT calling ListToolsAsync first — this is the core scenario from issue #1577
        client.AddKnownTools([CreateToolWithHeaders()]);

        // Call the tool
        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["region"] = "us-west-2", ["priority"] = 42 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        // Verify that Mcp-Param-* headers were captured by the server
        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        Assert.True(headers.ContainsKey("Mcp-Param-Region"), "Expected Mcp-Param-Region header to be sent");
        Assert.Equal("us-west-2", headers["Mcp-Param-Region"]);
        Assert.True(headers.ContainsKey("Mcp-Param-Priority"), "Expected Mcp-Param-Priority header to be sent");
        Assert.Equal("42", headers["Mcp-Param-Priority"]);
    }

    [Theory]
    [InlineData("42.0", "42")]                                 // decimal body form canonicalized
    [InlineData("-7.00", "-7")]                                // trailing zeros canonicalized
    [InlineData("-0.0", "0")]                                  // negative zero canonicalized
    [InlineData("4.2e1", "42")]                                // exponent body form canonicalized
    [InlineData("9007199254740991", "9007199254740991")]      // max safe integer preserved exactly
    [InlineData("-9007199254740991", "-9007199254740991")]    // min safe integer preserved exactly
    public async Task CallTool_EmitsCanonicalIntegerHeader(string bodyValue, string expectedHeader)
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        client.AddKnownTools([CreateToolWithHeaders()]);

        // Pass the raw JSON number so the body retains the exact form under test.
        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?>
            {
                ["region"] = "us-west-2",
                ["priority"] = JsonDocument.Parse(bodyValue).RootElement,
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        var headers = _capturedHeaders.Values.First();
        Assert.Equal(expectedHeader, headers["Mcp-Param-Priority"]);
    }

    [Theory]
    [InlineData("9007199254740993")]    // 2^53 + 1, above the safe range
    [InlineData("-9007199254740993")]   // -(2^53 + 1), below the safe range
    [InlineData("42.5")]                // not a whole number
    [InlineData("12e-1")]               // 1.2 in exponent form, not a whole number
    [InlineData("42.0000000000000000000000000001")] // high-precision fraction (decimal would round this to 42)
    public async Task CallTool_ThrowsForInvalidIntegerHeaderValue(string bodyValue)
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        client.AddKnownTools([CreateToolWithHeaders()]);

        // Values outside the JavaScript safe integer range (or non-integral) must be rejected
        // before the request is sent.
        await Assert.ThrowsAsync<McpException>(async () => await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?>
            {
                ["region"] = "us-west-2",
                ["priority"] = JsonDocument.Parse(bodyValue).RootElement,
            },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(_capturedHeaders);
    }

    [Fact]
    public async Task CallToolWithoutRegisterOrList_DoesNotSendMcpParamHeaders()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Call the tool without AddKnownTools or ListToolsAsync — no Mcp-Param-* headers should be sent
        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["region"] = "us-west-2", ["priority"] = 42 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        // Verify that NO Mcp-Param-* headers were sent
        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        Assert.Empty(headers);

        // Verify that a cache miss warning IS logged for HTTP transport
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning &&
            log.Message.Contains("not found in cache during tools/call"));
    }

    [Fact]
    public async Task AddKnownTools_SurvivesListToolsAsync_HeadersStillSent()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Register the tool first
        client.AddKnownTools([CreateToolWithHeaders()]);

        // Call ListToolsAsync — server returns empty list, but registered tool should survive
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Call the registered tool — Mcp-Param-* headers should still be sent
        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["region"] = "eu-central-1", ["priority"] = 99 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        // Verify headers were sent
        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        Assert.True(headers.ContainsKey("Mcp-Param-Region"), "Expected Mcp-Param-Region header after ListToolsAsync");
        Assert.Equal("eu-central-1", headers["Mcp-Param-Region"]);
        Assert.True(headers.ContainsKey("Mcp-Param-Priority"), "Expected Mcp-Param-Priority header after ListToolsAsync");
        Assert.Equal("99", headers["Mcp-Param-Priority"]);
    }

    [Fact]
    public async Task RemoveKnownTools_ThenCallTool_NoMcpParamHeaders()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Register then remove — headers should no longer be sent
        client.AddKnownTools([CreateToolWithHeaders()]);
        client.RemoveKnownTools(["my_tool"]);

        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["region"] = "us-east-1", ["priority"] = 1 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        // Verify no Mcp-Param-* headers were sent after removal
        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        Assert.Empty(headers);
    }

    private static Tool CreateToolWithSingleHeader(string toolName, string headerName)
    {
        var schemaJson = $$"""
            {
                "type": "object",
                "properties": {
                    "value": {
                        "type": "string",
                        "x-mcp-header": "{{headerName}}"
                    }
                },
                "required": ["value"]
            }
            """;

        return new Tool
        {
            Name = toolName,
            InputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone(),
        };
    }

    [Fact]
    public async Task AddKnownTools_ServerReturnsEmptyList_RegisteredToolStillUsedForHeaders()
    {
        // Staleness test: register foo → server returns [] → ListToolsAsync → call foo → headers still sent
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Register tool, then ListToolsAsync returns empty list from server
        client.AddKnownTools([CreateToolWithHeaders()]);
        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Call the registered tool — headers should still be sent (sticky registration)
        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["region"] = "ap-southeast-1", ["priority"] = 5 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        Assert.True(headers.ContainsKey("Mcp-Param-Region"), "Expected Mcp-Param-Region after server returned empty list");
        Assert.Equal("ap-southeast-1", headers["Mcp-Param-Region"]);
        Assert.True(headers.ContainsKey("Mcp-Param-Priority"), "Expected Mcp-Param-Priority after server returned empty list");
        Assert.Equal("5", headers["Mcp-Param-Priority"]);
    }

    [Fact]
    public async Task AddKnownTools_ReRegisterOverwrite_LastWriteWinsHeaders()
    {
        // Last-write-wins: register foo with schema A → register foo with schema B → call → headers reflect schema B
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions { ProtocolVersion = "2025-11-25" }, loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        // Register with header "SchemaA", then overwrite with "SchemaB"
        client.AddKnownTools([CreateToolWithSingleHeader("my_tool", "SchemaA")]);
        client.AddKnownTools([CreateToolWithSingleHeader("my_tool", "SchemaB")]);

        var result = await client.CallToolAsync(
            "my_tool",
            new Dictionary<string, object?> { ["value"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);

        Assert.Single(_capturedHeaders);
        var headers = _capturedHeaders.Values.First();
        // SchemaA header should NOT be present
        Assert.False(headers.ContainsKey("Mcp-Param-SchemaA"), "SchemaA header should have been overwritten");
        // SchemaB header SHOULD be present (last write wins)
        Assert.True(headers.ContainsKey("Mcp-Param-SchemaB"), "Expected Mcp-Param-SchemaB from overwritten registration");
        Assert.Equal("test", headers["Mcp-Param-SchemaB"]);
    }
}
