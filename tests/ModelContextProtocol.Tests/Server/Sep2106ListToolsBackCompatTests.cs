using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// SEP-2106 backward-compat at the tools/list emission boundary. Clients negotiating a
/// pre-2026-07-28 protocol version must still receive the legacy
/// <c>{"type":"object","properties":{"result":&lt;schema&gt;},"required":["result"]}</c>
/// envelope for non-object output schemas. In-memory storage stays natural; only the
/// wire emission flips on the negotiated version.
/// </summary>
public class Sep2106ListToolsBackCompatTests : ClientServerTestBase
{
    private const string LegacyProtocolVersion = "2025-11-25";
    private const string Sep2106ProtocolVersion = "2026-07-28";

    public Sep2106ListToolsBackCompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
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
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_NullableObjectTool_NormalizesTypeArrayForLegacyClients(string serverProtocolVersion, bool expectNormalized)
    {
        // For clients on protocol versions older than 2026-07-28, type:["object","null"]
        // must be emitted as plain type:"object" (those versions accept object schemas but
        // not type-arrays, and the value side stays a plain object, no envelope). SEP-2106
        // clients (2026-07-28+) see the natural type-array intact per the SEP's
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

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_DuplicateTypeRefsTool_RewritesRefsWhenWrapped(string serverProtocolVersion, bool expectWrapped)
    {
        // List<ContactInfo> has the same type (PhoneNumber) at two locations, so the schema
        // exporter emits $ref pointers for deduplication. For legacy clients the array schema
        // is wrapped under properties.result, which must rewrite those $refs to stay resolvable.
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_duplicate_refs");

        AssertRefsValidForWire(schema, expectWrapped);
    }

    [Theory]
    [InlineData(LegacyProtocolVersion, true)]
    [InlineData(Sep2106ProtocolVersion, false)]
    public async Task ListTools_RecursiveTypeRefsTool_RewritesRefsWhenWrapped(string serverProtocolVersion, bool expectWrapped)
    {
        // List<TreeNode> is recursive: Children's items emit a $ref back to the TreeNode
        // definition (e.g. "#/items"). When wrapped for legacy clients that becomes
        // "#/properties/result/items" and must still resolve.
        ConfigureServerWithTools(serverProtocolVersion);
        await using var client = await CreateMcpClientForServer(new() { ProtocolVersion = serverProtocolVersion });

        JsonElement schema = await GetOutputSchemaAsync(client, "return_recursive_refs");

        AssertRefsValidForWire(schema, expectWrapped);
    }

    /// <summary>
    /// Asserts the emitted schema's <c>$ref</c> pointers are consistent with the wire shape:
    /// when wrapped (legacy clients) the array schema is enveloped under
    /// <c>properties.result</c> and every <c>$ref</c> is rewritten under that prefix; otherwise
    /// (SEP-2106 clients) the natural array schema is emitted with its original refs. Either
    /// way, every <c>$ref</c> must resolve against the emitted schema root.
    /// </summary>
    private static void AssertRefsValidForWire(JsonElement schema, bool expectWrapped)
    {
        JsonNode schemaNode = JsonNode.Parse(schema.GetRawText())!;

        if (expectWrapped)
        {
            Assert.Equal("object", schema.GetProperty("type").GetString());
            int rewritten = AssertAllRefsStartWith(schemaNode, "#/properties/result");
            Assert.True(rewritten > 0, "Expected at least one $ref rewritten under #/properties/result.");
        }
        else
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            Assert.Equal(0, CountRefsStartingWith(schemaNode, "#/properties/result"));
        }

        int resolvable = AssertAllRefsResolvable(schemaNode, schemaNode);
        Assert.True(resolvable > 0, "Expected at least one resolvable $ref in the schema.");
    }

    private static int AssertAllRefsStartWith(JsonNode? node, string expectedPrefix)
    {
        int count = 0;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out JsonNode? refNode) &&
                refNode?.GetValue<string>() is string refValue)
            {
                Assert.StartsWith(expectedPrefix, refValue);
                count++;
            }

            foreach (var property in obj)
            {
                count += AssertAllRefsStartWith(property.Value, expectedPrefix);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                count += AssertAllRefsStartWith(item, expectedPrefix);
            }
        }

        return count;
    }

    private static int CountRefsStartingWith(JsonNode? node, string prefix)
    {
        int count = 0;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out JsonNode? refNode) &&
                refNode?.GetValue<string>() is string refValue &&
                refValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                count++;
            }

            foreach (var property in obj)
            {
                count += CountRefsStartingWith(property.Value, prefix);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                count += CountRefsStartingWith(item, prefix);
            }
        }

        return count;
    }

    private static int AssertAllRefsResolvable(JsonNode root, JsonNode? node)
    {
        int count = 0;
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out JsonNode? refNode) &&
                refNode?.GetValue<string>() is string refValue &&
                refValue.StartsWith("#", StringComparison.Ordinal))
            {
                var resolved = ResolveJsonPointer(root, refValue);
                Assert.True(resolved is not null, $"$ref \"{refValue}\" does not resolve to a valid node in the schema.");
                count++;
            }

            foreach (var property in obj)
            {
                count += AssertAllRefsResolvable(root, property.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                count += AssertAllRefsResolvable(root, item);
            }
        }

        return count;
    }

    private static JsonNode? ResolveJsonPointer(JsonNode root, string pointer)
    {
        if (pointer == "#")
        {
            return root;
        }

        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        JsonNode? current = root;
        string[] segments = pointer.Substring(2).Split('/');
        foreach (string segment in segments)
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out current))
                {
                    return null;
                }
            }
            else if (current is JsonArray arr && int.TryParse(segment, out int index) && index >= 0 && index < arr.Count)
            {
                current = arr[index];
            }
            else
            {
                return null;
            }
        }

        return current;
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
            McpServerTool.Create(() => new List<ContactInfo>
            {
                new()
                {
                    WorkPhones = [new() { Number = "555-0100", Type = "work" }],
                    HomePhones = [new() { Number = "555-0200", Type = "home" }],
                }
            }, new() { Name = "return_duplicate_refs", UseStructuredContent = true, SerializerOptions = serializerOptions }),
            McpServerTool.Create(() => new List<TreeNode>
            {
                new() { Name = "root", Children = [new() { Name = "child" }] }
            }, new() { Name = "return_recursive_refs", UseStructuredContent = true, SerializerOptions = serializerOptions }),
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

    // ContactInfo has two properties of the same type (PhoneNumber), which makes the schema
    // exporter emit $ref pointers for deduplication.
    private sealed class PhoneNumber
    {
        public string? Number { get; set; }
        public string? Type { get; set; }
    }

    private sealed class ContactInfo
    {
        public List<PhoneNumber>? WorkPhones { get; set; }
        public List<PhoneNumber>? HomePhones { get; set; }
    }

    // Recursive type: Children's items emit a $ref back to the TreeNode definition.
    private sealed class TreeNode
    {
        public string? Name { get; set; }
        public List<TreeNode>? Children { get; set; }
    }
}
