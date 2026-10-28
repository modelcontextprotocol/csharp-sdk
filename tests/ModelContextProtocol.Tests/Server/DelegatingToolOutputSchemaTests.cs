using Json.Schema;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingToolOutputSchemaTests
{
    [Theory]
    [InlineData("2025-06-18", 1, false)]
    [InlineData("2025-06-18", 2, false)]
    [InlineData("2025-06-18", 1, true)]
    [InlineData("2025-06-18", 2, true)]
    [InlineData("2025-11-25", 1, false)]
    [InlineData("2025-11-25", 2, false)]
    [InlineData("2025-11-25", 1, true)]
    [InlineData("2025-11-25", 2, true)]
    [InlineData("2026-07-28", 1, false)]
    [InlineData("2026-07-28", 2, false)]
    [InlineData("2026-07-28", 1, true)]
    [InlineData("2026-07-28", 2, true)]
    public async Task ListTools_DelegatingTool_MatchesStructuredContent(string protocolVersion, int depth, bool overrideProtocolTool)
    {
        McpServerTool inner = McpServerTool.Create(() => 72, new() { Name = "count", UseStructuredContent = true });
        McpServerTool tool = inner;
        for (int i = 0; i < depth; i++)
        {
            tool = new DelegatingTool(tool);
        }

        if (overrideProtocolTool)
        {
            tool = new DescribedTool(tool);
        }

        string originalSchema = tool.ProtocolTool.OutputSchema!.Value.GetRawText();
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            bool legacy = protocolVersion != "2026-07-28";

            Assert.Equal(legacy ? "object" : "integer", schema.GetProperty("type").GetString());
            if (legacy)
            {
                schema = schema.GetProperty("properties").GetProperty("result");
                Assert.Equal("integer", schema.GetProperty("type").GetString());
                Assert.Equal(72, content.GetProperty("result").GetInt32());
            }
            else
            {
                Assert.Equal(72, content.GetInt32());
            }

            if (overrideProtocolTool)
            {
                Assert.Equal("Wrapped count", listed.Description);
                Assert.Equal(0, schema.GetProperty("minimum").GetInt32());
            }
        });

        Assert.Equal(originalSchema, tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("integer", inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingCustomTool_PreservesItsOutputSchema(string protocolVersion)
    {
        McpServerTool tool = new DelegatingTool(new DelegatingTool(new CustomTool()));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("integer", listed.ProtocolTool.OutputSchema!.Value.GetProperty("type").GetString());
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(72, result.StructuredContent!.Value.GetInt32());
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_HandlesOptionalOutputSchema(string protocolVersion, bool useStructuredContent)
    {
        JsonElement nullableSchema = ParseJson("""{"type":["object","null"]}""");
        McpServerTool inner = McpServerTool.Create(() => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "optional",
            UseStructuredContent = useStructuredContent,
            OutputSchema = useStructuredContent ? nullableSchema : null,
        });
        await WithClientAsync(new DelegatingTool(inner), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            if (!useStructuredContent)
            {
                Assert.Null(listed.ProtocolTool.OutputSchema);
                Assert.Null(result.StructuredContent);
            }
            else
            {
                JsonElement type = listed.ProtocolTool.OutputSchema!.Value.GetProperty("type");
                if (protocolVersion == "2026-07-28")
                {
                    Assert.Equal(JsonValueKind.Array, type.ValueKind);
                }
                else
                {
                    Assert.Equal("object", type.GetString());
                }

                Assert.Equal("test", result.StructuredContent!.Value.GetProperty("name").GetString());
                Assert.Equal(JsonValueKind.Array, inner.ProtocolTool.OutputSchema!.Value.GetProperty("type").ValueKind);
            }
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_ComposedSchemaMatchesStructuredContent(string protocolVersion, bool innerUsesComposition)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        JsonElement innerSchema = innerUsesComposition ? composedSchema : objectSchema;
        JsonElement outerSchema = innerUsesComposition ? objectSchema : composedSchema;
        McpServerTool inner = McpServerTool.Create(() => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "composed",
            UseStructuredContent = true,
            OutputSchema = innerSchema,
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), outerSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            bool legacy = protocolVersion != "2026-07-28";
            bool wrapped = legacy && innerUsesComposition;

            if (legacy)
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
            }
            else
            {
                Assert.Equal(outerSchema.GetRawText(), schema.GetRawText());
            }

            Assert.Equal("test", (wrapped ? content.GetProperty("result") : content).GetProperty("name").GetString());
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText());
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            JsonElement invalidContent = ParseJson(wrapped ? """{"result":{"name":""}}""" : """{"name":""}""");
            Assert.False(advertisedSchema.Evaluate(invalidContent).IsValid);
        });

        Assert.Equal(outerSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(innerSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingTool_BooleanSchemaMatchesStructuredContent(string protocolVersion)
    {
        McpServerTool inner = McpServerTool.Create(() => ParseJson("{}"), new()
        {
            Name = "boolean_schema",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"type":"object"}"""),
        });
        McpServerTool tool = new SchemaOverrideTool(inner, ParseJson("true"));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;

            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(JsonValueKind.True, schema.ValueKind);
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(schema.TryGetProperty("properties", out _));
            }

            Assert.Equal(JsonValueKind.Object, content.ValueKind);
            Assert.True(JsonSchema.FromText(schema.GetRawText()).Evaluate(content).IsValid);
        });

        Assert.Equal(JsonValueKind.True, tool.ProtocolTool.OutputSchema!.Value.ValueKind);
    }

    [Theory]
    [InlineData("2025-06-18", "sync", false)]
    [InlineData("2025-06-18", "sync", true)]
    [InlineData("2025-06-18", "task", false)]
    [InlineData("2025-06-18", "task", true)]
    [InlineData("2025-06-18", "valueTask", false)]
    [InlineData("2025-06-18", "valueTask", true)]
    [InlineData("2025-11-25", "sync", false)]
    [InlineData("2025-11-25", "sync", true)]
    [InlineData("2025-11-25", "task", false)]
    [InlineData("2025-11-25", "task", true)]
    [InlineData("2025-11-25", "valueTask", false)]
    [InlineData("2025-11-25", "valueTask", true)]
    [InlineData("2026-07-28", "sync", false)]
    [InlineData("2026-07-28", "sync", true)]
    [InlineData("2026-07-28", "task", false)]
    [InlineData("2026-07-28", "task", true)]
    [InlineData("2026-07-28", "valueTask", false)]
    [InlineData("2026-07-28", "valueTask", true)]
    public async Task ListTools_DelegatingTool_ExplicitResultMatchesStructuredContent(string protocolVersion, string returnKind, bool overrideSchema)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new()
        {
            Content = [new TextContentBlock { Text = "explicit response" }],
            StructuredContent = ParseJson("""{"name":"test"}"""),
            IsError = false,
        };
        Delegate callback = returnKind switch
        {
            "sync" => new Func<CallToolResult>(() => response),
            "task" => new Func<Task<CallToolResult>>(() => Task.FromResult(response)),
            "valueTask" => new Func<ValueTask<CallToolResult>>(() => new(response)),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerTool inner = McpServerTool.Create(callback, new()
        {
            Name = "explicit_result",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
        });
        McpServerTool tool = new DelegatingTool(inner);
        if (overrideSchema)
        {
            tool = new SchemaOverrideTool(tool, objectSchema);
        }

        JsonElement originalSchema = tool.ProtocolTool.OutputSchema!.Value;
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;

            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(originalSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty("result", out _));
            }

            Assert.Equal(response.StructuredContent!.Value.GetRawText(), content.GetRawText());
            Assert.Equal("explicit response", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.False(result.IsError);
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText());
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
        });

        Assert.Equal(originalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_DelegatingTool_CustomResultMarshallingMatchesStructuredContent(string protocolVersion)
    {
        JsonElement objectSchema = ParseJson("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""");
        AIFunction function = AIFunctionFactory.Create(() => new CallToolResult(), new()
        {
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            MarshalResult = static (_, _, _) => new ValueTask<object?>(ParseJson("""{"name":"test"}""")),
        });
        McpServerTool inner = McpServerTool.Create(function, new()
        {
            Name = "marshalled_result",
            UseStructuredContent = true,
            OutputSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}"""),
        });
        await WithClientAsync(new SchemaOverrideTool(inner, objectSchema), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;
            if (protocolVersion != "2026-07-28")
            {
                content = content.GetProperty("result");
            }

            Assert.Equal("test", content.GetProperty("name").GetString());
            Assert.True(JsonSchema.FromText(listed.ProtocolTool.OutputSchema!.Value.GetRawText()).Evaluate(result.StructuredContent.Value).IsValid);
        });
    }

    private static JsonElement ParseJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task WithClientAsync(McpServerTool tool, string protocolVersion, Func<McpClient, Task> action)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TestConstants.DefaultTimeout);
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        await using var server = McpServer.Create(
            new StreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream()),
            new McpServerOptions { ToolCollection = new() { tool } });
        Task serverTask = server.RunAsync(cts.Token);
        try
        {
            var clientOptions = TestMcpClient.CreateOptions();
            clientOptions.ProtocolVersion = protocolVersion;
            await using var client = await TestMcpClient.CreateAsync(
                new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
                clientOptions, cancellationToken: cts.Token);
            await action(client);
        }
        finally
        {
            await cts.CancelAsync();
            await serverTask;
        }
    }

    private sealed class DelegatingTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

    private sealed class SchemaOverrideTool(McpServerTool inner, JsonElement schema) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = schema,
        };
    }

    private sealed class DescribedTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            Description = "Wrapped count",
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = ParseJson("""{"type":"integer","minimum":0}"""),
        };
    }

    private sealed class CustomTool : McpServerTool
    {
        public override IReadOnlyList<object> Metadata => [];

        public override Tool ProtocolTool { get; } = new()
        {
            Name = "custom",
            InputSchema = ParseJson("""{"type":"object"}"""),
            OutputSchema = ParseJson("""{"type":"integer"}"""),
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            new(new CallToolResult { StructuredContent = ParseJson("72") });
    }
}
