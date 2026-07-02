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
    public async Task Server_AcceptsUnionIntegerCanonicalForm()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
    public async Task Server_EchoesRequestId_WhenMcpNameHeaderMissing()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

        // The body is a well-formed JSON-RPC request with a numeric id. Header validation fails
        // because the Mcp-Name header is omitted, but the id was already parsed from the body, so the
        // error response MUST echo it rather than returning id:null (JSON-RPC 2.0 §5).
        var callBody = """
            {"jsonrpc":"2.0","id":1001,"method":"tools/call","params":{"name":"header_test","arguments":{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}}}
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callBody, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        // Mcp-Name intentionally omitted.

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("id").ValueKind);
        Assert.Equal(1001, root.GetProperty("id").GetInt64());
        Assert.Equal((int)McpErrorCode.HeaderMismatch, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Server_EchoesRequestId_WhenMcpNameHeaderMismatchesBody()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

        // Body declares the tool name "header_test" but the Mcp-Name header says something else.
        // The id was parsed from the body, so the mismatch error must still echo it.
        var callBody = """
            {"jsonrpc":"2.0","id":"req-7","method":"tools/call","params":{"name":"header_test","arguments":{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}}}
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callBody, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "other_tool");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.String, root.GetProperty("id").ValueKind);
        Assert.Equal("req-7", root.GetProperty("id").GetString());
        Assert.Equal((int)McpErrorCode.HeaderMismatch, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Server_EchoesRequestId_WhenSessionIdHeaderRejectedUnderJuly2026Protocol()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

        // Under the 2026-07-28 revision (SEP-2567) an Mcp-Session-Id header is rejected. The body was
        // parsed successfully, so this post-parse error must echo the request id too.
        var callBody = """
            {"jsonrpc":"2.0","id":2002,"method":"tools/call","params":{"name":"header_test","arguments":{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}}}
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callBody, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "us-west1");
        request.Headers.Add("Mcp-Param-Priority", "42");
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");
        request.Headers.Add("Mcp-Session-Id", "some-session");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("id").ValueKind);
        Assert.Equal(2002, root.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Server_ReturnsNullId_WhenRequestBodyIsMalformed()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

        // When the body can't be parsed the id can't be read, so id:null is the correct, conformant
        // response. This guards against over-correcting the fix for the parsed-id case.
        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task Server_AcceptsBase64EncodedHeaderWithControlChars()
    {
        await StartAsync();
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
    public async Task Server_SkipsHeaderValidation_ForLegacyVersion()
    {
        await StartAsync();
        await InitializeWithLegacyVersionAsync();

        // With the legacy version, Mcp-Param-* headers are NOT validated even if mismatched
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        // Send the WRONG header value. This should still succeed because the version is legacy.
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
        await InitializeWithJuly2026ProtocolVersionAsync();

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
    public void SupportsStandardHeaders_CorrectlyGatesVersions(string? version, bool expected)
    {
        Assert.Equal(expected, McpHttpHeaders.SupportsStandardHeaders(version));
    }

    #endregion

    #region Helpers

    private async Task InitializeWithJuly2026ProtocolVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContent(InitializeRequestJuly2026Protocol);
        request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "initialize");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Starting with the 2026-07-28 protocol revision (SEP-2567), Streamable HTTP does not return a
        // mcp-session-id header. Subsequent requests carry MCP-Protocol-Version=2026-07-28
        // so each one is handled independently.
    }

    private async Task InitializeWithLegacyVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Server is stateless by default (SEP-2567), so initializing with the legacy protocol does not return
        // a mcp-session-id header. Subsequent requests are independent, just like requests on the 2026-07-28 revision.
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private long _lastRequestId = 1;

    private string CallTool(string toolName, string arguments = "{}")
    {
        var id = Interlocked.Increment(ref _lastRequestId);
        return $$$"""
            {"jsonrpc":"2.0","id":{{{id}}},"method":"tools/call","params":{"name":"{{{toolName}}}","arguments":{{{arguments}}}}}
            """;
    }

    private static string InitializeRequest => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
        """;

    private static string InitializeRequestJuly2026Protocol => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2026-07-28","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
        """;

    #endregion
}

