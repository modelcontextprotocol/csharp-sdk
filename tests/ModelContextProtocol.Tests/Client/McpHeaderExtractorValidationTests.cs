using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Tests for SEP-2243 x-mcp-header validation changes:
/// - RFC 9110 tchar validation for header names
/// - "number" type rejection (only integer/string/boolean allowed)
/// - Nested property support for x-mcp-header annotations
/// </summary>
public class McpHeaderExtractorValidationTests : ClientServerTestBase
{
    public McpHeaderExtractorValidationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Valid baseline tool
        mcpServerBuilder.WithTools([McpServerTool.Create(
            (string input) => $"echo {input}",
            new() { Name = "ValidTool" })]);

        // Tool with "number" type (should be rejected per updated SEP-2243)
        var numberTool = McpServerTool.Create((string x) => x, new() { Name = "NumberTypeTool" });
        numberTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "value": { "type": "number", "x-mcp-header": "Value" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([numberTool]);

        // Tool with "integer" type (should be accepted)
        var integerTool = McpServerTool.Create((string x) => x, new() { Name = "IntegerTypeTool" });
        integerTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "count": { "type": "integer", "x-mcp-header": "Count" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([integerTool]);

        // Tool with non-tchar header name (should be rejected)
        var nonTcharTool = McpServerTool.Create((string x) => x, new() { Name = "BadTcharTool" });
        nonTcharTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "region": { "type": "string", "x-mcp-header": "Region(1)" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nonTcharTool]);

        // Tool with valid nested x-mcp-header
        var nestedValidTool = McpServerTool.Create((string x) => x, new() { Name = "NestedValidTool" });
        nestedValidTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "config": { "type": "object", "properties": { "region": { "type": "string", "x-mcp-header": "Region" } } } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nestedValidTool]);

        // Tool with invalid nested x-mcp-header (colon in header name)
        var nestedInvalidTool = McpServerTool.Create((string x) => x, new() { Name = "NestedInvalidTool" });
        nestedInvalidTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "config": { "type": "object", "properties": { "region": { "type": "string", "x-mcp-header": "Invalid:Header" } } } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nestedInvalidTool]);

        // Tool with duplicate header names across nesting levels
        var duplicateTool = McpServerTool.Create((string x) => x, new() { Name = "DuplicateHeaderTool" });
        duplicateTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "topRegion": { "type": "string", "x-mcp-header": "Region" }, "nested": { "type": "object", "properties": { "innerRegion": { "type": "string", "x-mcp-header": "region" } } } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([duplicateTool]);

        // Tool with nested "number" type (should be rejected)
        var nestedNumberTool = McpServerTool.Create((string x) => x, new() { Name = "NestedNumberTool" });
        nestedNumberTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "config": { "type": "object", "properties": { "threshold": { "type": "number", "x-mcp-header": "Threshold" } } } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nestedNumberTool]);

        // Tool with a nullable union type ["string", "null"] (should be accepted)
        var nullableUnionTool = McpServerTool.Create((string x) => x, new() { Name = "NullableUnionTool" });
        nullableUnionTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "region": { "type": ["string", "null"], "x-mcp-header": "Region" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nullableUnionTool]);

        // Tool with a union type containing a disallowed type ["number", "null"] (should be rejected)
        var numberUnionTool = McpServerTool.Create((string x) => x, new() { Name = "NumberUnionTool" });
        numberUnionTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "value": { "type": ["number", "null"], "x-mcp-header": "Value" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([numberUnionTool]);

        // Tool with a "null"-only union type (should be rejected: no allowed primitive present)
        var nullOnlyTool = McpServerTool.Create((string x) => x, new() { Name = "NullOnlyTool" });
        nullOnlyTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "value": { "type": ["null"], "x-mcp-header": "Value" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([nullOnlyTool]);

        // Tool whose annotated property omits "type" (should be accepted: type is unknown, not invalid)
        var missingTypeTool = McpServerTool.Create((string x) => x, new() { Name = "MissingTypeTool" });
        missingTypeTool.ProtocolTool.InputSchema = JsonDocument.Parse("""
            { "type": "object", "properties": { "region": { "x-mcp-header": "Region" } } }
            """).RootElement.Clone();
        mcpServerBuilder.WithTools([missingTypeTool]);
    }

    [Fact]
    public async Task ListToolsAsync_NumberType_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "NumberTypeTool");

        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Message.Contains("NumberTypeTool") &&
            log.Message.Contains("excluded"));
    }

    [Fact]
    public async Task ListToolsAsync_IntegerType_AcceptsTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "IntegerTypeTool");
    }

    [Fact]
    public async Task ListToolsAsync_NonTcharHeaderName_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "BadTcharTool");
    }

    [Fact]
    public async Task ListToolsAsync_NestedValidHeader_AcceptsTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "NestedValidTool");
    }

    [Fact]
    public async Task ListToolsAsync_NestedInvalidHeader_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "NestedInvalidTool");
    }

    [Fact]
    public async Task ListToolsAsync_NestedDuplicateHeaders_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "DuplicateHeaderTool");
    }

    [Fact]
    public async Task ListToolsAsync_NestedNumberType_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "NestedNumberTool");
    }

    [Fact]
    public async Task ListToolsAsync_NullableUnionType_AcceptsTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "NullableUnionTool");
    }

    [Fact]
    public async Task ListToolsAsync_NumberUnionType_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "NumberUnionTool");
    }

    [Fact]
    public async Task ListToolsAsync_NullOnlyUnionType_ExcludesTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "ValidTool");
        Assert.DoesNotContain(tools, t => t.Name == "NullOnlyTool");
    }

    [Fact]
    public async Task ListToolsAsync_MissingType_AcceptsTool()
    {
        await using var client = await CreateMcpClientForServer();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(tools, t => t.Name == "MissingTypeTool");
    }
}
