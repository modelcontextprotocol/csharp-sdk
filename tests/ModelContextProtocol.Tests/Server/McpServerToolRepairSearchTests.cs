using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using ModelContextProtocol.Tests.Utils;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
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
