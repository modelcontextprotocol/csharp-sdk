using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
#if NET
    [Theory]
    [InlineData(8, true)]
    [InlineData(64, false)]
    [InlineData(128, false)]
    [InlineData(128, true)]
    public async Task StructuredOutput_Search_BoundsWideSchemaAllocations(int width, bool recoverable)
    {
        JsonArray alternatives = [];
        for (int i = 0; i < width; i++)
        {
            alternatives.Add((JsonNode)new JsonObject
            {
                ["required"] = new JsonArray($"missing{i}_" + new string('x', 512)),
            });
        }
        if (recoverable)
        {
            alternatives.Add((JsonNode)new JsonObject { ["required"] = new JsonArray("note") });
        }

        McpServerTool tool = CreateStructuredOutputTestTool(
            new { Value = "kept", Note = (string?)null }, new JsonObject { ["anyOf"] = alternatives }.ToJsonString());

        await Task.Run(() =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Task<JsonElement> invocation = InvokeStructuredOutputTestTool(tool);
            Assert.True(invocation.IsCompleted);
            JsonElement result = invocation.GetAwaiter().GetResult();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.Current.TestOutputHelper?.WriteLine($"Allocated {allocated:N0} bytes for {width} branches.");

            Assert.Equal("kept", result.GetProperty("value").GetString());
            Assert.Equal(recoverable && width == 8, result.TryGetProperty("note", out _));
            if (width != 8)
            {
                Assert.Equal("""{"value":"kept"}""", result.GetRawText());
            }
            Assert.True(allocated < 32 * 1024 * 1024, $"Recovery allocated {allocated:N0} bytes for {width} branches.");
        }, TestContext.Current.CancellationToken);
    }
#endif

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    [InlineData("allOf")]
    [InlineData("reference")]
    [InlineData("crossParents")]
    [InlineData("crossReferences")]
    public async Task StructuredOutput_Search_QualifiesEnclosingItemAssertions(string shape)
    {
        string item = shape switch
        {
            "oneOf" => """{"properties":{"a":true},"additionalProperties":false,"oneOf":[{"required":["b"]},{"required":["a"]}]}""",
            "allOf" => """{"allOf":[{"anyOf":[{"required":["a"]},{"required":["b"]}]},{"not":{"required":["b"]}}]}""",
            "reference" => """{"$ref":"#/$defs/choice","properties":{"a":true},"additionalProperties":false}""",
            _ => """{"properties":{"a":true},"additionalProperties":false,"anyOf":[{"required":["a"]},{"required":["b"]}]}""",
        };
        var output = Enumerable.Range(0, 129).Select(_ => new { A = (string?)null, B = (string?)null, C = (string?)null }).ToArray();
        string schema = shape switch
        {
            "crossParents" => """{"allOf":[{"items":{"anyOf":[{"required":["b"]},{"required":["a"]}]}},{"items":{"required":["c"],"not":{"required":["b"]}}}]}""",
            "crossReferences" => """{"allOf":[{"items":{"$ref":"#/$defs/choice"}},{"items":{"$ref":"#/$defs/guard"}}],"$defs":{"choice":{"$id":"choice","anyOf":[{"required":["b"]},{"required":["a"]}]},"guard":{"$id":"guard","required":["c"],"not":{"required":["b"]}}}}""",
            _ => $$$$"""{"items":{{{{item}}}},"$defs":{"choice":{"anyOf":[{"required":["a"]},{"required":["b"]}]}}}""",
        };
        McpServerTool tool = CreateStructuredOutputTestTool(output, schema);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), child =>
        {
            Assert.Equal(JsonValueKind.Null, child.GetProperty("a").ValueKind);
            Assert.False(child.TryGetProperty("b", out _));
            Assert.Equal(shape is "crossParents" or "crossReferences", child.TryGetProperty("c", out _));
        });
    }

    [Theory]
    [InlineData("object")]
    [InlineData("array")]
    [InlineData("reference")]
    [InlineData("allOf")]
    [InlineData("anyOf")]
    [InlineData("contains")]
    [InlineData("becomesEvaluated")]
    [InlineData("validAlternative")]
    public async Task StructuredOutput_Search_DoesNotRepairEvaluatedOptionalMembers(string shape)
    {
        var first = new { Note = (string?)null, Real = (string?)null };
        var second = new { Note = (string?)null, Real = (string?)null };
        bool array = shape is "array" or "contains";
        object output = array ? new[] { first, second } : new { Evaluated = first, Other = second };
        string schema = shape switch
        {
            "array" => """{"prefixItems":[{"type":"object"}],"unevaluatedItems":{"required":["note"]}}""",
            "contains" => """{"prefixItems":[{"properties":{"real":true}}],"contains":{"required":["real"]},"unevaluatedItems":{"required":["note"]}}""",
            "reference" => """{"$id":"https://example.test/unevaluated","$defs":{"rule":{"properties":{"evaluated":true},"unevaluatedProperties":{"required":["note"]}}},"allOf":[{"$ref":"#/$defs/rule"}]}""",
            "allOf" => """{"allOf":[{"properties":{"evaluated":true}}],"unevaluatedProperties":{"required":["note"]}}""",
            "anyOf" => """{"anyOf":[{"properties":{"evaluated":true}}],"unevaluatedProperties":{"required":["note"]}}""",
            "becomesEvaluated" => """{"properties":{"evaluated":{"required":["real"]}},"unevaluatedProperties":{"required":["note"]}}""",
            "validAlternative" => """{"anyOf":[{"properties":{"evaluated":true}},{"properties":{"other":{"required":["real"]}}}],"unevaluatedProperties":{"required":["note"]}}""",
            _ => """{"properties":{"evaluated":{"type":"object"}},"unevaluatedProperties":{"required":["note"]}}""",
        };
        if (shape == "contains")
        {
            output = new[] { new { Note = (string?)null, Real = (string?)"present" }, first };
        }
        McpServerTool tool = CreateStructuredOutputTestTool(output, schema);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        JsonElement evaluated = array ? result[0] : result.GetProperty("evaluated");
        JsonElement other = array ? result[1] : result.GetProperty("other");
        Assert.False(evaluated.TryGetProperty("note", out _));
        Assert.Equal(JsonValueKind.Null, other.GetProperty("note").ValueKind);
        Assert.False(other.TryGetProperty("real", out _));
    }

    [Fact]
    public async Task StructuredOutput_Search_ComposesIndependentExclusiveChoicesAtSamePath()
    {
        var output = new
        {
            A0 = (string?)null,
            B0 = (string?)null,
            A1 = (string?)null,
            B1 = (string?)null,
            A2 = (string?)null,
            B2 = (string?)null,
            A3 = (string?)null,
            B3 = (string?)null,
            A4 = (string?)null,
            B4 = (string?)null,
        };
        JsonArray allOf = [];
        for (int i = 0; i < 5; i++)
        {
            allOf.Add(JsonNode.Parse($$"""{"oneOf":[{"required":["a{{i}}"]},{"required":["b{{i}}"]}]}"""));
        }

        McpServerTool tool = CreateStructuredOutputTestTool(output, new JsonObject { ["allOf"] = allOf }.ToJsonString());
        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        for (int i = 0; i < 5; i++)
        {
            Assert.NotEqual(result.TryGetProperty($"a{i}", out _), result.TryGetProperty($"b{i}", out _));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_Search_PreservesCallerScopeForBranchValidation(bool dynamicReference, bool siblingReference)
    {
        string reference = dynamicReference ? "\"$dynamicRef\":\"#entry\"" : "\"$ref\":\"https://example.test/root#entry\"";
        if (siblingReference)
        {
            reference += ",\"$ref\":\"#/$defs/childRule\"";
        }

        string schema = $$$$"""
            {
              "$schema":"https://json-schema.org/draft/2020-12/schema",
              "$id":"https://example.test/root", "$dynamicAnchor":"entry",
              "required":["note"],
              "properties":{"items":{"items":{"$ref":"base"}}},
              "$defs":{"base":{
                "$id":"base","$dynamicAnchor":"entry",
                "$defs":{"childRule":{"required":["marker"]}},
                "oneOf":[
                  {"required":["tag","good"],"properties":{"tag":{"const":"0"},"good":true,"child":{ {{{{reference}}}} }},"additionalProperties":false},
                  {"required":["tag","bad"],"properties":{"tag":{"const":"1"},"bad":true,"child":{ {{{{reference}}}} }},"additionalProperties":false}
                ]
              }}
            }
            """;
        var output = new
        {
            Note = (string?)null,
            Items = Enumerable.Range(0, 5).Select(_ => new
            {
                Tag = "0",
                Good = (string?)null,
                Bad = (string?)null,
                Child = new { Note = (string?)null, Marker = (string?)null },
            }).ToArray(),
        };
        McpServerTool tool = CreateStructuredOutputTestTool(output, schema);
        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("note").ValueKind);
        foreach (JsonElement item in result.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Null, item.GetProperty("good").ValueKind);
            Assert.False(item.TryGetProperty("bad", out _));
            Assert.Equal(JsonValueKind.Null, item.GetProperty("child").GetProperty("note").ValueKind);
            Assert.Equal(siblingReference, item.GetProperty("child").TryGetProperty("marker", out _));
        }
    }

    [Fact]
    public async Task StructuredOutput_Search_RepairsLargeMixedTagArray()
    {
        const string schema = """
            {"type":"array","items":{"oneOf":[
              {"required":["tag","good"],"properties":{"tag":{"const":"0"},"good":true},"additionalProperties":false},
              {"required":["tag","bad"],"properties":{"tag":{"const":"1"},"bad":true},"additionalProperties":false}
            ]}}
            """;
        var output = Enumerable.Range(0, 10_000).Select(i => new
        {
            Tag = (i % 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Good = (string?)null,
            Bad = (string?)null,
        }).ToArray();
        McpServerTool tool = CreateStructuredOutputTestTool(output, schema);
        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.Equal(output.Length, result.GetArrayLength());
        for (int i = 0; i < output.Length; i++)
        {
            Assert.Equal(JsonValueKind.Null, result[i].GetProperty(i % 2 == 0 ? "good" : "bad").ValueKind);
            Assert.False(result[i].TryGetProperty(i % 2 == 0 ? "bad" : "good", out _));
        }
    }

    [Fact]
    public async Task StructuredOutput_Search_DistinguishesDynamicCallersForSharedBranches()
    {
        const string schema = """
            {
              "$schema":"https://json-schema.org/draft/2020-12/schema",
              "$id":"https://example.test/root",
              "properties":{"left":{"$ref":"left"},"right":{"$ref":"right"}},
              "$defs":{
                "left":{"$id":"left","$dynamicAnchor":"entry","required":["leftNote"],
                  "properties":{"items":{"items":{"$ref":"base"}}}},
                "right":{"$id":"right","$dynamicAnchor":"entry","required":["rightNote"],
                  "properties":{"items":{"items":{"$ref":"base"}}}},
                "base":{"$id":"base","$dynamicAnchor":"entry","oneOf":[
                  {"required":["tag","good"],"properties":{"tag":{"const":"0"},"good":true,"child":{"$dynamicRef":"#entry"}},"additionalProperties":false},
                  {"required":["tag","bad"],"properties":{"tag":{"const":"1"},"bad":true,"child":{"$dynamicRef":"#entry"}},"additionalProperties":false}
                ]}
              }
            }
            """;
        var items = Enumerable.Range(0, 5).Select(_ => new
        {
            Tag = "0",
            Good = (string?)null,
            Bad = (string?)null,
            Child = new { LeftNote = (string?)null, RightNote = (string?)null },
        }).ToArray();
        McpServerTool tool = CreateStructuredOutputTestTool(new
        {
            Left = new { LeftNote = (string?)null, Items = items },
            Right = new { RightNote = (string?)null, Items = items },
        }, schema);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        foreach (string side in new[] { "left", "right" })
        {
            foreach (JsonElement item in result.GetProperty(side).GetProperty("items").EnumerateArray())
            {
                JsonElement child = item.GetProperty("child");
                Assert.Equal(JsonValueKind.Null, child.GetProperty(side + "Note").ValueKind);
                Assert.False(child.TryGetProperty(side == "left" ? "rightNote" : "leftNote", out _));
                Assert.False(item.TryGetProperty("bad", out _));
            }
        }
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(25, false)]
    public async Task StructuredOutput_Search_TraversesSharedReferenceDagOnce(int depth, bool sourceHasValue)
    {
        string property = sourceHasValue ? "note" : "absent";
        JsonObject definitions = new() { ["s0"] = JsonNode.Parse($$"""{"required":["{{property}}"]}""") };
        for (int i = 1; i <= depth; i++)
        {
            definitions[$"s{i}"] = JsonNode.Parse($$"""{"allOf":[{"$ref":"#/$defs/s{{i - 1}}"},{"$ref":"#/$defs/s{{i - 1}}"}]}""");
        }

        McpServerTool tool = CreateStructuredOutputTestTool(
            new { Note = (string?)null },
            new JsonObject { ["$ref"] = $"#/$defs/s{depth}", ["$defs"] = definitions }.ToJsonString());
        JsonElement result = await Task.Run(
            () => InvokeStructuredOutputTestTool(tool), TestContext.Current.CancellationToken)
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        if (sourceHasValue)
        {
            Assert.Equal(JsonValueKind.Null, result.GetProperty("note").ValueKind);
            // Detailed diagnostics duplicate results for every successful DAG branch.
            JsonSchema schema = JsonSchema.Build(
                tool.ProtocolTool.OutputSchema!.Value, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            Assert.True(schema.Evaluate(result, new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid);
        }
        else
        {
            Assert.False(result.TryGetProperty("absent", out _));
        }
    }
}
