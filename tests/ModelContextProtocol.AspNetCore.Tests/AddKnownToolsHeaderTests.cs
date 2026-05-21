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
                        ProtocolVersion = "DRAFT-2026-v1",
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

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
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

    [Fact]
    public async Task CallToolWithoutRegisterOrList_DoesNotSendMcpParamHeaders()
    {
        await StartAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new("http://localhost:5000/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
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

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
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

        await using var client = await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory,
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
}
