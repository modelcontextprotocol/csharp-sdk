using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for SEP-2243 HTTP header standardization features:
/// - Custom Mcp-Param-* header validation
/// - Tab/control character encoding
/// - Numeric precision in header values
/// - Empty string header validation
/// - Invalid header character rejection
/// </summary>
public class HttpHeaderConformanceTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(HttpHeaderConformanceTests),
                Version = "1.0",
            };
#pragma warning disable MCPEXP002
            options.RequestHandlers =
            [
                new McpServerRequestHandler
                {
                    Method = "extension/get",
                    RoutingNameParameter = "itemId",
                    Handler = static (_, _) => new ValueTask<JsonNode?>(
                        new JsonObject { ["resultType"] = "complete" }),
                },
            ];
#pragma warning restore MCPEXP002
        }).WithTools(Tools).WithHttpTransport();

        _app = Builder.Build();
        _app.MapMcp();
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

    // Create a tool with x-mcp-header annotations in the schema.
    // We set InputSchema directly because TransformSchemaNode doesn't provide
    // property-level path context for lambda-based tool creation.
    private static McpServerTool[] Tools { get; } = [CreateHeaderTestTool(), CreateUnionHeaderTestTool()];

    private static readonly JsonSerializerOptions s_reflectionOptions = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static McpServerTool CreateHeaderTestTool()
    {
        var tool = McpServerTool.Create(
            [McpServerTool(Name = "header_test")]
            static (string region, long priority, bool verbose, string emptyVal) =>
                $"region={region},priority={priority},verbose={verbose},empty={emptyVal}",
            new McpServerToolCreateOptions { SerializerOptions = s_reflectionOptions });

        using var doc = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "region": { "type": "string", "x-mcp-header": "Region" },
                "priority": { "type": "integer", "x-mcp-header": "Priority" },
                "verbose": { "type": "boolean", "x-mcp-header": "Verbose" },
                "emptyVal": { "type": "string", "x-mcp-header": "EmptyVal" }
              },
              "required": ["region", "priority", "verbose", "emptyVal"]
            }
            """);
        tool.ProtocolTool.InputSchema = doc.RootElement.Clone();

        return tool;
    }

    // A tool whose integer header parameter uses a JSON Schema union type (["integer", "null"]).
    private static McpServerTool CreateUnionHeaderTestTool()
    {
        var tool = McpServerTool.Create(
            [McpServerTool(Name = "union_test")]
            static (long priority) => $"priority={priority}",
            new McpServerToolCreateOptions { SerializerOptions = s_reflectionOptions });

        using var doc = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "priority": { "type": ["integer", "null"], "x-mcp-header": "Priority" }
              },
              "required": ["priority"]
            }
            """);
        tool.ProtocolTool.InputSchema = doc.RootElement.Clone();

        return tool;
    }

    #region Server-side validation tests

    [Fact]
    public async Task Server_UsesCustomHandlerRoutingNameMetadata()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContent(
            """{"jsonrpc":"2.0","id":2,"method":"extension/get","params":{"itemId":"item-42","_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"TestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}""");
        request.Headers.Add(McpHttpHeaders.ProtocolVersion, "2026-07-28");
        request.Headers.Add(McpHttpHeaders.Method, "extension/get");
        request.Headers.Add(McpHttpHeaders.Name, "item-42");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsUnionIntegerCanonicalForm()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Union-typed (["integer","null"]) parameter: header carries canonical "42" while the body
        // carries the decimal form 42.0. The server must treat the union type as integer and match.
        var callJson = CallTool("union_test", """{"priority":42.0}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "union_test");
        request.Headers.Add("Mcp-Param-Priority", "42");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsUnionIntegerOutsideSafeRange()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        var callJson = CallTool("union_test", """{"priority":9007199254740993}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "union_test");
        request.Headers.Add("Mcp-Param-Priority", "9007199254740993");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsExponentBodyMatchingDecimalHeader()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Body carries the integer in exponent form (1e2 = 100); header carries the decimal "100".
        var callJson = CallTool("header_test", """{"region":"test","priority":1e2,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", "100");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsWhitespaceAroundMcpNameHeaderValue()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Per SEP-2243: servers MUST accept extra whitespace around header values
        // and compare the trimmed value to the request body.
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", "  header_test  ");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsWhitespaceAroundMcpMethodHeaderValue()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Per SEP-2243: servers MUST accept extra whitespace around header values
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "  tools/call  ");
        request.Headers.TryAddWithoutValidation("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_ValidatesEmptyStringHeaderValue_AgainstBodyValue()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Send a tools/call with an empty string param that has an x-mcp-header.
        // The header should be present with an empty value, matching the body's empty string.
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsHeaderMismatch_WhenEmptyHeaderDoesNotMatchBody()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Send a tools/call where the body has a non-empty value but the header is empty
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":"some-value"}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsBase64EncodedHeaderWithControlChars()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Encode a value with a newline control character using Base64
        var valueWithNewline = "line1\nline2";
        var encodedValue = McpHeaderEncoder.EncodeValue(valueWithNewline);

        var callJson = CallTool("header_test", $$"""{"region":"{{valueWithNewline.Replace("\n", "\\n")}}","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", encodedValue!);
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_AcceptsMaxSafeIntegerWithFullPrecision()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // The maximum safe integer (2^53 - 1) must be accepted, and compared exactly without
        // losing precision through a double conversion.
        const long maxSafeInt = 9007199254740991L;
        var callJson = CallTool("header_test", $$"""{"region":"test","priority":{{maxSafeInt}},"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", maxSafeInt.ToString());
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("9007199254740993")]   // 2^53 + 1, just outside the safe range
    [InlineData("-9007199254740993")]  // -(2^53 + 1), just outside the safe range
    [InlineData("100000000000000000000000000000000000000")] // far beyond decimal range
    [InlineData("1e100")] // exponent form far beyond the safe range
    public async Task Server_RejectsIntegerOutsideSafeRange(string outOfRangeValue)
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Per SEP-2243 integer values MUST be within the JavaScript safe integer range.
        // A matching header and body that are both outside the range must still be rejected.
        var callJson = CallTool("header_test", $$"""{"region":"test","priority":{{outOfRangeValue}},"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", outOfRangeValue);
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("42", "42")]       // "42" header vs 42 body -> exact integer match
    [InlineData("42.0", "42")]     // "42.0" header vs 42 body -> numeric equivalence
    [InlineData("42", "42.0")]     // "42" header vs 42.0 body (decimal form from another SDK) -> numeric equivalence
    [InlineData("42", "4.2e1")]    // "42" header vs 4.2e1 body (exponent form) -> numeric equivalence
    [InlineData("420e-1", "42")]   // "420e-1" header vs 42 body -> numeric equivalence
    public async Task Server_AcceptsNumericEquivalentHeaderValues(string headerValue, string bodyValue)
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // bodyValue is inserted as a raw JSON numeric literal so that forms such as "42.0" and
        // "4.2e1" are preserved in the body exactly as another SDK might serialize them.
        var callJson = CallTool("header_test", $$"""{"region":"test","priority":{{bodyValue}},"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", headerValue);
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("42.5")]                                 // fractional value for an integer parameter
    [InlineData("12e-1")]                                // 1.2 in exponent form
    [InlineData("42.0000000000000000000000000001")]      // high-precision fraction that decimal would round to 42
    public async Task Server_RejectsNonIntegerValue_EvenWhenHeaderAndBodyMatch(string nonIntegerValue)
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // For an integer-typed parameter a non-whole numeric value is invalid and must be rejected
        // even when the header and body strings are byte-for-byte identical (it must not slip through
        // the ordinal comparison).
        var callJson = CallTool("header_test", $$"""{"region":"test","priority":{{nonIntegerValue}},"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", nonIntegerValue);
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsNonNumericMismatch_ForIntegerParam()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Header says "99" but body says priority:42 — must reject even with numeric comparison
        var callJson = CallTool("header_test", """{"region":"test","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", "99");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Server_SkipsHeaderValidation_ForInitializeHandshakeVersion()
    {
        await StartAsync();
        await InitializeWithInitializeHandshakeVersionAsync();

        // With the initialize-handshake version, Mcp-Param-* headers are NOT validated even if mismatched.
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""", includePerRequestMetadata: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        // Send the WRONG header value. This should still succeed because the version uses initialize.
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "WRONG-VALUE");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8EncodedHeaderValue()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Create a separate HttpClient that sends raw UTF-8 bytes in Mcp-* headers
        // instead of properly base64-encoding non-ASCII values.
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = SocketsHttpHandler.ConnectCallback,
            RequestHeaderEncodingSelector = (headerName, _) =>
                headerName.StartsWith("Mcp-", StringComparison.OrdinalIgnoreCase)
                    ? Encoding.UTF8
                    : null
        };

        using var utf8Client = new HttpClient(handler);
        ConfigureHttpClient(utf8Client);
        utf8Client.DefaultRequestHeaders.Accept.Add(new("application/json"));
        utf8Client.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));

        // Send a tools/call with raw UTF-8 non-ASCII in the Mcp-Name header.
        // Kestrel reads header bytes as Latin-1, so the UTF-8 bytes for "café☕"
        // will be garbled and won't match the body value, causing rejection.
        var callJson = CallTool("café☕", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        // Raw UTF-8 non-ASCII value in Mcp-Name — server must reject this
        request.Headers.TryAddWithoutValidation("Mcp-Name", "café☕");
        request.Headers.TryAddWithoutValidation("Mcp-Param-Region", "us-west1");
        request.Headers.TryAddWithoutValidation("Mcp-Param-Priority", "42");
        request.Headers.TryAddWithoutValidation("Mcp-Param-Verbose", "false");
        request.Headers.TryAddWithoutValidation("Mcp-Param-EmptyVal", "");

        using var response = await utf8Client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8InProtocolVersionMeta_WithBadRequest()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // The 0xFF byte can never appear in valid UTF-8. The JSON reader accepts it inside a string token,
        // and the failure only surfaces when the server converts the _meta protocol version to a string.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28",
            "\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}}}}");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/list");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8InInitializeProtocolVersion_WithBadRequest()
    {
        await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25",
            "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"}}}");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 1);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8PropertyNameInInitializeParams_WithBadRequest()
    {
        await StartAsync();

        // Reading any property of a JSON object converts all of its property names to strings,
        // so one unreadable name breaks the lookup of params.protocolVersion.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"x",
            "\":1}}");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 1);
    }

    [Theory]
    [InlineData("params")]
    [InlineData("_meta")]
    [InlineData("arguments")]
    public async Task Server_RejectsInvalidUtf8PropertyName_WithBadRequest(string location)
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // Each body has one property name that is "x" followed by a 0xFF byte, placed in the named object.
        // The other property names in that object are valid, but they cannot be read either.
        const string Arguments = "\"region\":\"us-west1\",\"priority\":42,\"verbose\":false,\"emptyVal\":\"\"";
        const string MetaEntries = "\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}";
        const string Start = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{";
        (string before, string after) = location switch
        {
            "params" => (
                $"{Start}\"name\":\"header_test\",\"arguments\":{{{Arguments}}},\"_meta\":{{{MetaEntries}}},\"x",
                "\":1}}"),
            "_meta" => (
                $"{Start}\"name\":\"header_test\",\"arguments\":{{{Arguments}}},\"_meta\":{{{MetaEntries},\"x",
                "\":1}}}"),
            "arguments" => (
                $"{Start}\"name\":\"header_test\",\"arguments\":{{{Arguments},\"x",
                $"\":1}},\"_meta\":{{{MetaEntries}}}}}}}"),
            _ => throw new ArgumentOutOfRangeException(nameof(location)),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(before, after);
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    [Fact]
    public async Task Server_RejectsLoneSurrogateInMcpNameBodyValue_WithBadRequest()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // An escaped lone surrogate is valid JSON text, but it cannot be converted to a valid string.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContent("""
            {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"\uD800","arguments":{},"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"TestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
            """);
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8InCustomHandlerRoutingValue_WithBadRequest()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // The extension/get handler routes on params.itemId, which is compared with the Mcp-Name header.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"extension/get\",\"params\":{\"itemId\":\"item-42",
            "\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}}}}");
        request.Headers.Add(McpHttpHeaders.ProtocolVersion, "2026-07-28");
        request.Headers.Add(McpHttpHeaders.Method, "extension/get");
        request.Headers.Add(McpHttpHeaders.Name, "item-42");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    [Fact]
    public async Task Server_TreatsInvalidUtf8ProtocolVersionMetaAsMissing_WithoutProtocolVersionHeader()
    {
        await StartAsync();

        // Without the MCP-Protocol-Version header the core server reads the _meta protocol version.
        // An unreadable value is treated as missing, so the request is served without per-request
        // metadata instead of failing with an internal error.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28",
            "\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}}}}");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rpcResponse = await ReadSseJsonRpcMessageAsync(response);
        Assert.Null(rpcResponse["error"]);
        Assert.NotNull(rpcResponse["result"]?["tools"]);
        Assert.Equal(2, rpcResponse["id"]!.GetValue<long>());
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8InMcpNameBodyValue_WithBadRequest()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // The Mcp-Name header is compared with params.name, which is converted to a string first.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"header_test",
            "\",\"arguments\":{},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}}}}");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    [Fact]
    public async Task Server_RejectsInvalidUtf8InCustomParamHeaderArgument_WithBadRequest()
    {
        await StartAsync();
        await ProbeWithJuly2026ProtocolVersionAsync();

        // The region argument is annotated with x-mcp-header, so it is converted to a string and compared
        // with the Mcp-Param-Region header.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContentWithInvalidUtf8(
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"header_test\",\"arguments\":{\"region\":\"us-west1",
            "\",\"priority\":42,\"verbose\":false,\"emptyVal\":\"\"},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"TestClient\",\"version\":\"1.0\"},\"io.modelcontextprotocol/clientCapabilities\":{}}}}");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertJsonRpcErrorAsync(response, McpErrorCode.InvalidRequest, expectedId: 2);
    }

    #endregion

    #region Client-side encoding tests (unit tests for McpHeaderEncoder)

    [Theory]
    [InlineData("hello\tworld")]
    [InlineData("col1\tcol2\tcol3")]
    public void Client_TabInValue_IsBase64Encoded(string value)
    {
        var encoded = McpHeaderEncoder.EncodeValue(value);
        Assert.NotNull(encoded);
        Assert.StartsWith("=?base64?", encoded);
        Assert.EndsWith("?=", encoded);

        // Verify round-trip
        var decoded = McpHeaderEncoder.DecodeValue(encoded);
        Assert.Equal(value, decoded);
    }

    [Theory]
    [InlineData("simple-text", false)]
    [InlineData("with space", false)]
    [InlineData("Hello, 世界", true)]
    [InlineData("line1\nline2", true)]
    [InlineData("\ttab-start", true)]
    [InlineData("mid\ttab", true)]
    [InlineData("control\x01char", true)]
    public void Client_EncodeValue_Base64OnlyWhenNeeded(string value, bool expectBase64)
    {
        var encoded = McpHeaderEncoder.EncodeValue(value);
        Assert.NotNull(encoded);

        if (expectBase64)
        {
            Assert.StartsWith("=?base64?", encoded);
        }
        else
        {
            Assert.DoesNotContain("=?base64?", encoded);
        }

        // All values must round-trip
        var decoded = McpHeaderEncoder.DecodeValue(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Client_EncodeValue_LargeInteger_PreservesFullPrecision()
    {
        // 2^53 + 1 cannot be represented exactly as a double
        var encoded = McpHeaderEncoder.EncodeValue(9007199254740993L);
        Assert.Equal("9007199254740993", encoded);
    }

    [Fact]
    public void Client_EncodeValue_Boolean_EncodesCorrectly()
    {
        Assert.Equal("true", McpHeaderEncoder.EncodeValue(true));
        Assert.Equal("false", McpHeaderEncoder.EncodeValue(false));
    }

    #endregion

    #region Version gating tests

    [Theory]
    [InlineData("2026-07-28", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-06-18", false)]
    [InlineData("2024-11-05", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void RequiresStandardHeaders_CorrectlyGatesVersions(string? version, bool expected)
    {
        Assert.Equal(expected, McpProtocolVersions.RequiresStandardHeaders(version));
    }

    #endregion

    #region Helpers

    private async Task ProbeWithJuly2026ProtocolVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContent(DiscoverRequestJuly2026Protocol);
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "server/discover");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Starting with the 2026-07-28 protocol revision, clients use server/discover and per-request
        // metadata instead of initialize.
    }

    private async Task InitializeWithInitializeHandshakeVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Server is stateless by default (SEP-2567), so initializing with an initialize-handshake protocol does not return
        // a mcp-session-id header. Subsequent requests are independent, just like requests on the 2026-07-28 revision.
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>
    /// Asserts a 400 response whose JSON-RPC error has the expected code and echoes the request id.
    /// </summary>
    private static async Task AssertJsonRpcErrorAsync(HttpResponseMessage response, McpErrorCode expectedCode, long expectedId)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal((int)expectedCode, json["error"]!["code"]!.GetValue<int>());
        Assert.Equal(expectedId, json["id"]!.GetValue<long>());
    }

    /// <summary>
    /// Reads the single JSON-RPC message from an SSE response.
    /// </summary>
    private static async Task<JsonNode> ReadSseJsonRpcMessageAsync(HttpResponseMessage response)
    {
        var responseStream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        List<string> messages = [];
        await foreach (var sseItem in SseParser.Create(responseStream).EnumerateAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal("message", sseItem.EventType);
            messages.Add(sseItem.Data);
        }

        return JsonNode.Parse(Assert.Single(messages))!;
    }

    /// <summary>
    /// Builds a JSON body that is <paramref name="before"/> + a single 0xFF byte + <paramref name="after"/>,
    /// so the body contains a string value that is not valid UTF-8.
    /// </summary>
    private static ByteArrayContent JsonContentWithInvalidUtf8(string before, string after)
    {
        byte[] body = [.. Encoding.UTF8.GetBytes(before), 0xFF, .. Encoding.UTF8.GetBytes(after)];
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        return content;
    }

    private long _lastRequestId = 1;

    private string CallTool(string toolName, string arguments = "{}", bool includePerRequestMetadata = true)
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        var meta = includePerRequestMetadata
            ? @",""_meta"":{""io.modelcontextprotocol/protocolVersion"":""2026-07-28"",""io.modelcontextprotocol/clientInfo"":{""name"":""TestClient"",""version"":""1.0""},""io.modelcontextprotocol/clientCapabilities"":{}}"
            : "";

        return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" +
            toolName + "\",\"arguments\":" + arguments + meta + "}}";
    }

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
        """;

    private static string DiscoverRequestJuly2026Protocol => """
        {"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"TestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
        """;

    #endregion
}
