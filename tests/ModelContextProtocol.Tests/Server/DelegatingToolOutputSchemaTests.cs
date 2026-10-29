using Json.Schema;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

public partial class DelegatingToolOutputSchemaTests
{
    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_TransformingDelegator_RecoversAgainstInnerSchema(string protocolVersion, bool overrideInnerSchema)
    {
        JsonElement required = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "transformed_note",
            UseStructuredContent = true,
            OutputSchema = overrideInnerSchema ? ParseJson("""{"type":"object"}""") : required,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        if (overrideInnerSchema)
        {
            inner = new SchemaOverrideTool(inner, required);
        }
        McpServerTool tool = new DelegatingTool(new PayloadTool(inner));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(result.StructuredContent!.Value).IsValid);
            Assert.Equal(JsonValueKind.Null, result.StructuredContent.Value.GetProperty("payload").GetProperty("note").ValueKind);
        });
    }

    [Theory]
    [InlineData("2025-06-18", false)]
    [InlineData("2025-06-18", true)]
    [InlineData("2025-11-25", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", false)]
    [InlineData("2026-07-28", true)]
    public async Task ListTools_DelegatingTool_RecoversAgainstEffectiveSchema(string protocolVersion, bool outerRequiresNull)
    {
        JsonElement requiredSchema = ParseJson("""{"type":"object","required":["note"],"properties":{"note":{"type":"null"}}}""");
        JsonElement optionalSchema = ParseJson("""{"type":"object","properties":{"note":{"type":"string"}}}""");
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "effective_note",
            UseStructuredContent = true,
            OutputSchema = outerRequiresNull ? optionalSchema : requiredSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), outerRequiresNull ? requiredSchema : optionalSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            Assert.Equal(outerRequiresNull, content.TryGetProperty("note", out _));
        });
    }

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
    public async Task ListTools_DelegatingTool_RecoversRequiredNullableMembers(string protocolVersion, bool innerUsesComposition)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"note":{"type":"null"}},"required":["note"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        JsonElement innerSchema = innerUsesComposition ? composedSchema : objectSchema;
        JsonElement outerSchema = innerUsesComposition ? objectSchema : composedSchema;
        McpServerTool inner = McpServerTool.Create(() => new { Note = (string?)null }, new()
        {
            Name = "required_note",
            UseStructuredContent = true,
            OutputSchema = innerSchema,
            SerializerOptions = new(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            },
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), outerSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement content = result.StructuredContent!.Value;

            Assert.True(ParseSchema(listed.ProtocolTool.OutputSchema!.Value).Evaluate(content).IsValid);
            if (innerUsesComposition && protocolVersion != "2026-07-28")
            {
                content = content.GetProperty("result");
            }
            Assert.Equal(JsonValueKind.Null, content.GetProperty("note").ValueKind);
        });

        Assert.Equal(innerSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(outerSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
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

    [Theory]
    [InlineData("2025-06-18", "supplied")]
    [InlineData("2025-06-18", "methodless")]
    [InlineData("2025-06-18", "sync")]
    [InlineData("2025-06-18", "task")]
    [InlineData("2025-06-18", "valueTask")]
    [InlineData("2025-06-18", "baseResult")]
    [InlineData("2025-11-25", "supplied")]
    [InlineData("2025-11-25", "methodless")]
    [InlineData("2025-11-25", "sync")]
    [InlineData("2025-11-25", "task")]
    [InlineData("2025-11-25", "valueTask")]
    [InlineData("2025-11-25", "baseResult")]
    [InlineData("2026-07-28", "supplied")]
    [InlineData("2026-07-28", "methodless")]
    [InlineData("2026-07-28", "sync")]
    [InlineData("2026-07-28", "task")]
    [InlineData("2026-07-28", "valueTask")]
    [InlineData("2026-07-28", "baseResult")]
    public async Task ListTools_UnknownMarshalling_ValidatesBothRuntimePaths(string protocolVersion, string creationKind)
    {
        JsonElement objectSchema = ParseJson(creationKind == "baseResult"
            ? """{"type":"object","properties":{"name":{"type":"string","minLength":1}}}"""
            : """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        object GetValue(bool returnExplicit) => returnExplicit ? response : ParseJson("""{"name":"test"}""");
        Delegate callback = creationKind switch
        {
            "supplied" or "methodless" or "sync" => new Func<bool, object>(returnExplicit => GetValue(returnExplicit)),
            "task" => new Func<bool, Task<object>>(returnExplicit => Task.FromResult(GetValue(returnExplicit))),
            "valueTask" => new Func<bool, ValueTask<object>>(returnExplicit => new(GetValue(returnExplicit))),
            "baseResult" => new Func<bool, Result>(returnExplicit => returnExplicit ? response : new ListToolsResult()),
            _ => throw new ArgumentOutOfRangeException(nameof(creationKind)),
        };
        McpServerToolCreateOptions options = new()
        {
            Name = "unknown_result",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
            SerializerOptions = new(McpJsonUtilities.DefaultOptions)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(McpJsonUtilities.DefaultOptions.TypeInfoResolver, ResultJsonContext.Default),
            },
        };
        McpServerTool inner;
        if (creationKind is "supplied" or "methodless")
        {
            AIFunction function = CreateIdentityFunction(callback);
            if (creationKind == "methodless")
            {
                function = new MethodlessFunction(function);
            }

            inner = McpServerTool.Create(function, options);
        }
        else
        {
            inner = McpServerTool.Create(callback, options);
        }

        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), objectSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText(), new BuildOptions { Dialect = Dialect.Draft202012 });
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(objectSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
            }

            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                if (!returnExplicit && creationKind == "baseResult")
                {
                    Assert.Empty(content.GetProperty("tools").EnumerateArray());
                }
                else
                {
                    Assert.Equal("test", content.GetProperty("name").GetString());
                }
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
        });

        Assert.Equal(objectSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18", "pointer")]
    [InlineData("2025-06-18", "self")]
    [InlineData("2025-06-18", "emptySelf")]
    [InlineData("2025-06-18", "encodedPointer")]
    [InlineData("2025-06-18", "resource")]
    [InlineData("2025-06-18", "nestedResource")]
    [InlineData("2025-06-18", "anchor")]
    [InlineData("2025-06-18", "dynamic")]
    [InlineData("2025-06-18", "dynamicPointer")]
    [InlineData("2025-06-18", "literal")]
    [InlineData("2025-06-18", "boolean")]
    [InlineData("2025-11-25", "pointer")]
    [InlineData("2025-11-25", "self")]
    [InlineData("2025-11-25", "emptySelf")]
    [InlineData("2025-11-25", "encodedPointer")]
    [InlineData("2025-11-25", "resource")]
    [InlineData("2025-11-25", "nestedResource")]
    [InlineData("2025-11-25", "anchor")]
    [InlineData("2025-11-25", "dynamic")]
    [InlineData("2025-11-25", "dynamicPointer")]
    [InlineData("2025-11-25", "literal")]
    [InlineData("2025-11-25", "boolean")]
    [InlineData("2026-07-28", "pointer")]
    [InlineData("2026-07-28", "self")]
    [InlineData("2026-07-28", "emptySelf")]
    [InlineData("2026-07-28", "encodedPointer")]
    [InlineData("2026-07-28", "resource")]
    [InlineData("2026-07-28", "nestedResource")]
    [InlineData("2026-07-28", "anchor")]
    [InlineData("2026-07-28", "dynamic")]
    [InlineData("2026-07-28", "dynamicPointer")]
    [InlineData("2026-07-28", "literal")]
    [InlineData("2026-07-28", "boolean")]
    public async Task ListTools_UnknownMarshalling_PreservesSchemaMeaning(string protocolVersion, string schemaKind)
    {
        (string schemaJson, string contentJson) = schemaKind switch
        {
            "pointer" => (
                """{"allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "self" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "emptySelf" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":""}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "encodedPointer" => (
                """{"allOf":[{"$ref":"#%2F$defs%2Fnode"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "resource" => (
                """{"$id":"urn:test:output","allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}}}""",
                """{"name":"test"}"""),
            "nestedResource" => (
                """{"allOf":[{"$ref":"#/$defs/node"}],"$defs":{"node":{"$id":"urn:test:node","type":"object","properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#"}},"required":["name"]}}}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "anchor" => (
                """{"$anchor":"node","allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$ref":"#node"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "dynamic" => (
                """{"$dynamicAnchor":"node","allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$dynamicRef":"#node"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "dynamicPointer" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"child":{"$dynamicRef":"#"}},"required":["name"]}""",
                """{"name":"test","child":{"name":"test"}}"""),
            "literal" => (
                """{"allOf":[{"type":"object"}],"properties":{"name":{"type":"string","minLength":1},"literal":{"const":{"$ref":"#"}},"$ref":{"const":"literal"}},"required":["name","literal","$ref"]}""",
                """{"name":"test","literal":{"$ref":"#"},"$ref":"literal"}"""),
            "boolean" => ("true", """{"name":"test"}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(schemaKind)),
        };
        schemaJson = schemaJson.Replace("urn:test:", $"urn:test:{protocolVersion}:");
        JsonElement naturalSchema = ParseJson(schemaJson);
        JsonElement expectedContent = ParseJson(contentJson);
        CallToolResult response = new() { StructuredContent = expectedContent };
        AIFunction function = CreateIdentityFunction(
            (bool returnExplicit) => returnExplicit ? (object)response : expectedContent);
        McpServerTool tool = new DelegatingTool(McpServerTool.Create(function, new()
        {
            Name = "schema_meaning",
            UseStructuredContent = true,
            OutputSchema = naturalSchema,
        }));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = JsonSchema.FromText(schema.GetRawText(), new BuildOptions { Dialect = Dialect.Draft202012 });
            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                Assert.Equal(expectedContent.GetRawText(), content.GetRawText());
            }

            if (schemaKind != "boolean")
            {
                string invalidContent = contentJson.Replace("\"test\"", "\"\"");
                Assert.False(advertisedSchema.Evaluate(ParseJson(invalidContent)).IsValid);
                if (protocolVersion != "2026-07-28")
                {
                    Assert.False(advertisedSchema.Evaluate(ParseJson($$"""{"result":{{invalidContent}}}""")).IsValid);
                }
            }
        });

        Assert.Equal(naturalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Invoke_ExplicitResult_DoesNotRequireFunctionSerializationMetadata(string protocolVersion)
    {
        CallToolResult response = new()
        {
            Content = [new TextContentBlock { Text = "explicit response" }],
            StructuredContent = ParseJson("""{"name":"test"}"""),
            IsError = true,
        };
        AIFunction function = AIFunctionFactory.Create(() => (object)response, new()
        {
            SerializerOptions = new JsonSerializerOptions { TypeInfoResolver = ObjectJsonContext.Default },
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        });
        McpServerTool tool = new DelegatingTool(McpServerTool.Create(function, new()
        {
            Name = "explicit_without_metadata",
            UseStructuredContent = true,
            OutputSchema = ParseJson("""{"allOf":[{"type":"object"}]}"""),
        }));
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var result = await client.CallToolAsync(tool.ProtocolTool.Name, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(response.StructuredContent!.Value.GetRawText(), result.StructuredContent!.Value.GetRawText());
            Assert.Equal("explicit response", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.True(result.IsError);
        });
    }

    [Theory]
    [MemberData(nameof(NullableExplicitResultCases))]
    public async Task ListTools_NullableExplicitResult_MatchesBothRuntimePaths(string protocolVersion, string returnKind)
    {
        JsonElement naturalSchema = ParseJson(
            """{"anyOf":[{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]},{"type":"null"}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        Delegate callback = returnKind switch
        {
            "sync" or "method" or "factory" or "supplied" =>
                new Func<bool, CallToolResult?>(returnNull => returnNull ? null : response),
            "task" => new Func<bool, Task<CallToolResult?>>(returnNull => Task.FromResult(returnNull ? null : response)),
            "valueTask" => new Func<bool, ValueTask<CallToolResult?>>(returnNull => new(returnNull ? null : response)),
            "broad" => new Func<bool, object?>(returnNull => returnNull ? null : response),
            "ordinary" => new Func<bool, JsonElement?>(returnNull => returnNull ? null : response.StructuredContent),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerToolCreateOptions options = new()
        {
            Name = "nullable_result",
            UseStructuredContent = true,
            OutputSchema = naturalSchema,
        };
        McpServerTool inner = returnKind switch
        {
            "method" => McpServerTool.Create(callback.Method, callback.Target, options),
            "factory" => McpServerTool.Create(callback.Method, _ => callback.Target!, options),
            "supplied" => McpServerTool.Create(CreateIdentityFunction(callback), options),
            _ => McpServerTool.Create(callback, options),
        };
        await WithClientAsync(new DelegatingTool(inner), protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            bool legacy = protocolVersion != "2026-07-28";
            if (!legacy)
            {
                Assert.Equal(naturalSchema.GetRawText(), schema.GetRawText());
            }

            foreach (bool returnNull in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnNull"] = returnNull,
                }, cancellationToken: TestContext.Current.CancellationToken);
                if (returnNull && !legacy)
                {
                    Assert.Null(result.StructuredContent);
                    Assert.Empty(result.Content);
                    continue;
                }

                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (returnNull || (legacy && returnKind == "ordinary"))
                {
                    content = content.GetProperty("result");
                }

                if (returnNull)
                {
                    Assert.Equal(JsonValueKind.Null, content.ValueKind);
                    Assert.Empty(result.Content);
                }
                else
                {
                    Assert.Equal("test", content.GetProperty("name").GetString());
                }
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
            if (returnKind is not ("ordinary" or "supplied" or "broad"))
            {
                Assert.False(advertisedSchema.Evaluate(ParseJson("""{"result":{"name":"test"}}""")).IsValid);
            }
        });

        Assert.Equal(naturalSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal("""{"name":"test"}""", response.StructuredContent!.Value.GetRawText());
    }

    public static IEnumerable<object[]> NullableExplicitResultCases()
    {
        foreach (string protocolVersion in new[] { "2025-06-18", "2025-11-25", "2026-07-28" })
        {
            foreach (string returnKind in new[] { "sync", "task", "valueTask", "method", "factory", "supplied", "broad", "ordinary" })
            {
                yield return [protocolVersion, returnKind];
            }
        }
    }

    [Theory]
    [MemberData(nameof(NullableRecursiveSchemaCases))]
    public async Task ListTools_NullableRecursiveSchema_PreservesDescendantConstraints(
        string protocolVersion, string returnKind, string schemaKind)
    {
        string refKeyword = schemaKind == "dynamic" ? "$dynamicRef" : "$ref";
        string reference = schemaKind is "anchor" or "dynamic" ? "#node" : "#";
        string identity = schemaKind switch
        {
            "resource" => $"\"$id\":\"urn:recursive:{protocolVersion}:{returnKind}\",",
            "anchor" => "\"$anchor\":\"node\",",
            "dynamic" => "\"$dynamicAnchor\":\"node\",",
            _ => "",
        };
        string objectMembers = $$$$"""
            "properties":{"name":{"type":"string","minLength":1},"child":{"{{{{refKeyword}}}}":"{{{{reference}}}}"},"children":{"type":"array","items":{"{{{{refKeyword}}}}":"{{{{reference}}}}"}}},"required":["name"],"additionalProperties":false
            """;
        string schemaJson = schemaKind == "nullableArray"
            ? $$"""{"type":["object","null"],{{objectMembers}}}"""
            : $$"""{"anyOf":[{"type":"null"},{"type":"object",{{objectMembers}}}]}""";
        JsonElement naturalSchema = ParseJson(schemaJson.Insert(1, identity));
        JsonElement expectedContent = ParseJson(
            """{"name":"root","child":{"name":"child","child":null},"children":[null,{"name":"leaf","child":null}]}""");
        Assert.True(ParseSchema(naturalSchema).Evaluate(expectedContent).IsValid);
        CallToolResult response = new() { StructuredContent = expectedContent };
        McpServerToolCreateOptions options = new()
        {
            Name = "recursive_result",
            UseStructuredContent = true,
            OutputSchema = schemaKind == "wrapper" ? ParseJson("""{"type":"object"}""") : naturalSchema,
        };
        McpServerTool inner = returnKind switch
        {
            "explicit" => McpServerTool.Create(() => response, options),
            "supplied" => McpServerTool.Create(CreateIdentityFunction(() => response), options),
            "ordinary" => McpServerTool.Create(() => expectedContent, options),
            _ => throw new ArgumentOutOfRangeException(nameof(returnKind)),
        };
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(inner), naturalSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            var result = await client.CallToolAsync(listed.Name, cancellationToken: TestContext.Current.CancellationToken);
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonElement content = result.StructuredContent!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            Assert.True(advertisedSchema.Evaluate(content).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson(content.GetRawText().Replace("\"child\":null", "\"child\":42"))).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson(content.GetRawText().Replace("\"root\"", "\"\""))).IsValid);
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(naturalSchema.GetRawText(), schema.GetRawText());
            }
            else
            {
                Assert.Equal("object", schema.GetProperty("type").GetString());
                Assert.False(advertisedSchema.Evaluate(ParseJson("null")).IsValid);
                if (returnKind == "ordinary" && schemaKind is not ("nullableArray" or "wrapper"))
                {
                    content = content.GetProperty("result");
                }
            }

            Assert.Equal(expectedContent.GetRawText(), content.GetRawText());
        });

        Assert.Equal(naturalSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(options.OutputSchema!.Value.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(expectedContent.GetRawText(), response.StructuredContent!.Value.GetRawText());
    }

    public static IEnumerable<object[]> NullableRecursiveSchemaCases()
    {
        foreach (string protocolVersion in new[] { "2025-06-18", "2025-11-25", "2026-07-28" })
        {
            foreach (string returnKind in new[] { "explicit", "supplied", "ordinary" })
            {
                foreach (string schemaKind in new[] { "composed", "nullableArray", "wrapper", "resource", "anchor", "dynamic" })
                {
                    yield return [protocolVersion, returnKind, schemaKind];
                }
            }
        }
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task ListTools_CustomDelegatingInvocation_MatchesBothRuntimePaths(string protocolVersion)
    {
        JsonElement objectSchema = ParseJson(
            """{"type":"object","properties":{"name":{"type":"string","minLength":1}},"required":["name"]}""");
        JsonElement composedSchema = ParseJson($$"""{"allOf":[{{objectSchema.GetRawText()}}]}""");
        CallToolResult response = new() { StructuredContent = ParseJson("""{"name":"test"}""") };
        McpServerTool inner = McpServerTool.Create((bool returnExplicit) => ParseJson("""{"name":"test"}"""), new()
        {
            Name = "custom_invocation",
            UseStructuredContent = true,
            OutputSchema = composedSchema,
        });
        McpServerTool tool = new SchemaOverrideTool(new DelegatingTool(new AlternateInvocationTool(inner, response)), objectSchema);
        await WithClientAsync(tool, protocolVersion, async client =>
        {
            var listed = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));
            JsonElement schema = listed.ProtocolTool.OutputSchema!.Value;
            JsonSchema advertisedSchema = ParseSchema(schema);
            foreach (bool returnExplicit in new[] { false, true })
            {
                var result = await client.CallToolAsync(listed.Name, new Dictionary<string, object?>
                {
                    ["returnExplicit"] = returnExplicit,
                }, cancellationToken: TestContext.Current.CancellationToken);
                JsonElement content = result.StructuredContent!.Value;
                Assert.True(advertisedSchema.Evaluate(content).IsValid);
                if (!returnExplicit && protocolVersion != "2026-07-28")
                {
                    content = content.GetProperty("result");
                }

                Assert.Equal("test", content.GetProperty("name").GetString());
            }

            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"name":""}""")).IsValid);
            Assert.False(advertisedSchema.Evaluate(ParseJson("""{"result":{"name":""}}""")).IsValid);
            if (protocolVersion == "2026-07-28")
            {
                Assert.Equal(objectSchema.GetRawText(), schema.GetRawText());
            }
        });

        Assert.Equal(objectSchema.GetRawText(), tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        Assert.Equal(composedSchema.GetRawText(), inner.ProtocolTool.OutputSchema!.Value.GetRawText());
    }

    [JsonSerializable(typeof(Result))]
    [JsonSerializable(typeof(object))]
    private sealed partial class ResultJsonContext : JsonSerializerContext;

    [JsonSerializable(typeof(object))]
    private sealed partial class ObjectJsonContext : JsonSerializerContext;

    private static AIFunction CreateIdentityFunction(Delegate callback) =>
        AIFunctionFactory.Create(callback, new()
        {
            SerializerOptions = McpJsonUtilities.DefaultOptions,
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        });

    private static JsonSchema ParseSchema(JsonElement schema) =>
        JsonSchema.FromText(schema.GetRawText(), new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new(),
        });

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

    private sealed class PayloadTool(McpServerTool inner) : DelegatingMcpServerTool(inner)
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = inner.ProtocolTool.Name,
            InputSchema = inner.ProtocolTool.InputSchema,
            OutputSchema = ParseJson($$$$"""{"type":"object","required":["payload"],"properties":{"payload":{{{{inner.ProtocolTool.OutputSchema!.Value.GetRawText()}}}}}}"""),
        };

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            CallToolResult result = await base.InvokeAsync(request, cancellationToken);
            result.StructuredContent = ParseJson($$"""{"payload":{{result.StructuredContent!.Value.GetRawText()}}}""");
            return result;
        }
    }

    private sealed class AlternateInvocationTool(McpServerTool inner, CallToolResult response) : DelegatingMcpServerTool(inner)
    {
        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            if (request.Params?.Arguments?.TryGetValue("returnExplicit", out JsonElement returnExplicit) is true && returnExplicit.GetBoolean())
            {
                return new(response);
            }

            return base.InvokeAsync(request, cancellationToken);
        }
    }

    private sealed class MethodlessFunction(AIFunction inner) : DelegatingAIFunction(inner)
    {
        public override MethodInfo? UnderlyingMethod => null;
    }

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
