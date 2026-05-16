using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// SEP-2106 backward-compat at the tools/list emission boundary. Clients negotiating a
/// pre-2026-06-30 protocol version must still receive the legacy
/// <c>{"type":"object","properties":{"result":&lt;schema&gt;},"required":["result"]}</c>
/// envelope for non-object output schemas. In-memory storage stays natural; only the
/// wire emission flips on the negotiated version.
/// </summary>
public class Sep2106ListToolsBackCompatTests : ClientServerTestBase
{
    private const string LegacyProtocolVersion = "2025-11-25";
    private const string DraftSep2106ProtocolVersion = "DRAFT-2026-06-v1";
    private const string Sep2106ProtocolVersion = "2026-06-30";

    public Sep2106ListToolsBackCompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(DraftSep2106ProtocolVersion, false)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_StringTool_WrapsOutputSchemaForLegacyClients(string serverProtocolVersion, bool expectWrapped)
    {
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_string");

        if (expectWrapped)
        {
            AssertResultEnvelope(schema, innerType: "string");
        }
        else
        {
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.False(schema.TryGetProperty("properties", out _));
        }
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(DraftSep2106ProtocolVersion, false)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_IntegerTool_WrapsOutputSchemaForLegacyClients(string serverProtocolVersion, bool expectWrapped)
    {
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_int");

        if (expectWrapped)
        {
            AssertResultEnvelope(schema, innerType: "integer");
        }
        else
        {
            Assert.Equal("integer", schema.GetProperty("type").GetString());
        }
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(DraftSep2106ProtocolVersion, false)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_ArrayTool_WrapsOutputSchemaForLegacyClients(string serverProtocolVersion, bool expectWrapped)
    {
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_array");

        if (expectWrapped)
        {
            AssertResultEnvelope(schema, innerType: "array");
        }
        else
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
        }
    }

    [Theory]
    [InlineData(LegacyProtocolVersion)]
    [InlineData(DraftSep2106ProtocolVersion)]
    [InlineData(Sep2106ProtocolVersion)]
    public async Task ListTools_ObjectTool_NeverWrapsOutputSchema(string serverProtocolVersion)
    {
        // Object-shaped schemas should be wire-identical across all protocol versions.
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_person");

        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.True(schema.GetProperty("properties").TryGetProperty("name", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("age", out _));
        Assert.False(schema.GetProperty("properties").TryGetProperty("result", out _));
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(DraftSep2106ProtocolVersion, false)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_NullableObjectTool_NormalizesTypeArrayForLegacyClients(string serverProtocolVersion, bool expectNormalized)
    {
        // For clients on protocol versions older than 2026-06-30, type:["object","null"]
        // must be emitted as plain type:"object" (those versions accept object schemas but
        // not type-arrays — and the value side stays a plain object, no envelope). SEP-2106
        // clients (2026-06-30+) see the natural type-array intact per the SEP's
        // any-JSON-Schema-2020-12 allowance.
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_nullable_object");

        Assert.False(schema.GetProperty("properties").TryGetProperty("result", out _),
            "type:['object','null'] schemas must not be re-wrapped in a result envelope.");

        JsonElement typeProperty = schema.GetProperty("type");
        if (expectNormalized)
        {
            // Legacy wire shape: ["object","null"] collapsed to plain "object" string.
            Assert.Equal(JsonValueKind.String, typeProperty.ValueKind);
            Assert.Equal("object", typeProperty.GetString());
        }
        else
        {
            // SEP-2106 wire shape: the natural type array passes through with both
            // members, in either order.
            Assert.Equal(JsonValueKind.Array, typeProperty.ValueKind);
            Assert.Equal(2, typeProperty.GetArrayLength());
            HashSet<string?> members = [];
            foreach (JsonElement entry in typeProperty.EnumerateArray())
            {
                members.Add(entry.GetString());
            }
            Assert.Contains("object", members);
            Assert.Contains("null", members);
        }
    }

    private void ConfigureServerWithTools(string protocolVersion)
    {
        JsonSerializerOptions serializerOptions = new()
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        JsonElement nullableObjectSchema = JsonDocument.Parse(
            """{"type":["object","null"],"properties":{"name":{"type":"string"}}}""").RootElement;

        ServiceCollection.Configure<McpServerOptions>(o => o.ProtocolVersion = protocolVersion);
        McpServerBuilder.WithTools(
        [
            McpServerTool.Create(() => "hello", new() { Name = "return_string", UseStructuredContent = true, SerializerOptions = serializerOptions }),
            McpServerTool.Create(() => 42, new() { Name = "return_int", UseStructuredContent = true, SerializerOptions = serializerOptions }),
            McpServerTool.Create(() => new[] { "a", "b" }, new() { Name = "return_array", UseStructuredContent = true, SerializerOptions = serializerOptions }),
            McpServerTool.Create(() => new Person("John", 27), new() { Name = "return_person", UseStructuredContent = true, SerializerOptions = serializerOptions }),
            McpServerTool.Create(() => new Person("John", 27), new() { Name = "return_nullable_object", UseStructuredContent = true, OutputSchema = nullableObjectSchema, SerializerOptions = serializerOptions }),
        ]);
        StartServer();
    }

    private static async Task<JsonElement> GetOutputSchemaAsync(McpClient client, string toolName)
    {
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var tool = tools.Single(t => t.Name == toolName);
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        return tool.ProtocolTool.OutputSchema.Value;
    }

    private static void AssertResultEnvelope(JsonElement schema, string innerType)
    {
        Assert.Equal("object", schema.GetProperty("type").GetString());

        JsonElement properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("result", out JsonElement inner));
        Assert.Equal(innerType, inner.GetProperty("type").GetString());

        JsonElement required = schema.GetProperty("required");
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
        Assert.Equal(1, required.GetArrayLength());
        Assert.Equal("result", required[0].GetString());
    }

    private sealed record Person(string Name, int Age);
}
