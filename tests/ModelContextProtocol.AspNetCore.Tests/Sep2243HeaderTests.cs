using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
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
public class Sep2243HeaderTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync()
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = nameof(Sep2243HeaderTests),
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
    private static McpServerTool[] Tools { get; } = [CreateHeaderTestTool()];

    private static readonly JsonSerializerOptions s_reflectionOptions = new()
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static McpServerTool CreateHeaderTestTool()
    {
        var tool = McpServerTool.Create(
            [McpServerTool(Name = "header_test")]
            static (string region, int priority, bool verbose, string emptyVal) =>
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

    #region Server-side validation tests

    [Fact]
    public async Task Server_ValidatesEmptyStringHeaderValue_AgainstBodyValue()
    {
        await StartAsync();
        await InitializeWithDraftVersionAsync();

        // Send a tools/call with an empty string param that has an x-mcp-header.
        // The header should be present with an empty value, matching the body's empty string.
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "DRAFT-2026-v1");
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
        await InitializeWithDraftVersionAsync();

        // Send a tools/call where the body has a non-empty value but the header is empty
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":"some-value"}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "DRAFT-2026-v1");
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
        await InitializeWithDraftVersionAsync();

        // Encode a value with a newline control character using Base64
        var valueWithNewline = "line1\nline2";
        var encodedValue = McpHeaderEncoder.EncodeValue(valueWithNewline);

        var callJson = CallTool("header_test", $$"""{"region":"{{valueWithNewline.Replace("\n", "\\n")}}","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "DRAFT-2026-v1");
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
    public async Task Server_AcceptsLargeIntegerWithFullPrecision()
    {
        await StartAsync();
        await InitializeWithDraftVersionAsync();

        // Use a large integer that would lose precision if converted through double
        // 2^53 + 1 = 9007199254740993 (cannot be represented exactly as double)
        const long largeInt = 9007199254740993L;
        var callJson = CallTool("header_test", $$"""{"region":"test","priority":{{largeInt}},"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        request.Headers.Add("MCP-Protocol-Version", "DRAFT-2026-v1");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "test");
        request.Headers.Add("Mcp-Param-Priority", largeInt.ToString());
        request.Headers.Add("Mcp-Param-Verbose", "false");
        request.Headers.Add("Mcp-Param-EmptyVal", "");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Server_SkipsHeaderValidation_ForNonDraftVersion()
    {
        await StartAsync();
        await InitializeWithNonDraftVersionAsync();

        // With non-draft version, Mcp-Param-* headers are NOT validated even if mismatched
        var callJson = CallTool("header_test", """{"region":"us-west1","priority":42,"verbose":false,"emptyVal":""}""");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(callJson, Encoding.UTF8, "application/json");
        // Send the WRONG header value — this should still succeed because version is non-draft
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "header_test");
        request.Headers.Add("Mcp-Param-Region", "WRONG-VALUE");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    [InlineData("DRAFT-2026-v1", true)]
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

    private async Task InitializeWithDraftVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = JsonContent(InitializeRequestDraft);
        request.Headers.Add("MCP-Protocol-Version", "DRAFT-2026-v1");
        request.Headers.Add("Mcp-Method", "initialize");

        using var response = await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
    }

    private async Task InitializeWithNonDraftVersionAsync()
    {
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");

        using var response = await HttpClient.PostAsync("", JsonContent(InitializeRequest), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessionId = Assert.Single(response.Headers.GetValues("mcp-session-id"));
        HttpClient.DefaultRequestHeaders.Remove("mcp-session-id");
        HttpClient.DefaultRequestHeaders.Add("mcp-session-id", sessionId);
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

    private static string InitializeRequestDraft => """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"DRAFT-2026-v1","capabilities":{},"clientInfo":{"name":"TestClient","version":"1.0"}}}
        """;

    #endregion
}
