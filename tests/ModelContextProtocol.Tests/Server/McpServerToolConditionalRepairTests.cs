using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
    [Fact]
    public async Task StructuredOutput_ConditionalRepair_LeavesExactContainsSearchExhaustionUnchanged()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 1, A = (string?)null, B = (string?)null }).ToArray(),
            """
            {
              "contains":{
                "if":{"properties":{"tag":{"const":0}}},
                "then":{"required":["a"],"not":{"required":["b"]}},
                "else":{"required":["b"],"not":{"required":["a"]}}
              },
              "minContains":64,
              "maxContains":64
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        // Exact-count subset search remains bounded; never emit an invalid partial repair.
        Assert.Equal(129, result.GetArrayLength());
        Assert.All(result.EnumerateArray(), item =>
        {
            Assert.Equal(1, item.GetProperty("tag").GetInt32());
            Assert.False(item.TryGetProperty("a", out _));
            Assert.False(item.TryGetProperty("b", out _));
        });
    }

    [Fact]
    public async Task StructuredOutput_ConditionalRepair_ComposesSiblingChoicesBeforeQualifyingAncestor()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new
            {
                Tag = 1,
                Left = new { A0 = (string?)null, B0 = (string?)null, A1 = (string?)null, B1 = (string?)null },
                Right = new { A0 = (string?)null, B0 = (string?)null, A1 = (string?)null, B1 = (string?)null },
            }).ToArray(),
            """
            {
              "items":{"oneOf":[
                {"properties":{"tag":{"const":0},"left":{"$ref":"#/$defs/zero"},"right":{"$ref":"#/$defs/zero"}},"required":["tag"]},
                {"properties":{"tag":{"const":1},"left":{"$ref":"#/$defs/one"},"right":{"$ref":"#/$defs/one"}},"required":["tag"]}
              ]},
              "$defs":{
                "zero":{"oneOf":[{"required":["a0"]},{"required":["b0"]}]},
                "one":{"oneOf":[{"required":["a1"]},{"required":["b1"]}]}
              }
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item =>
        {
            foreach (string name in new[] { "left", "right" })
            {
                JsonElement child = item.GetProperty(name);
                Assert.False(child.TryGetProperty("a0", out _));
                Assert.False(child.TryGetProperty("b0", out _));
                Assert.NotEqual(child.TryGetProperty("a1", out _), child.TryGetProperty("b1", out _));
            }
        });
    }

    [Theory]
    [InlineData("""{"properties":{"tag":{"const":1}}}""")]
    [InlineData("""{"type":"object","properties":{"tag":{"const":1}},"required":["marker","items"]}""")]
    [InlineData("""{"properties":{"tag":{"const":1}},"minProperties":3}""")]
    [InlineData("""{"properties":{"tag":{"const":1}},"maxProperties":3}""")]
    public async Task StructuredOutput_ConditionalRepair_ValidatesAncestorGuardWithoutLosingRequiredProperties(string guard)
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            new
            {
                Tag = 1,
                Marker = "present",
                Items = Enumerable.Range(0, 1024).Select(_ => new { A = (string?)null, B = (string?)null }).ToArray(),
            },
            """
            {
              "if":GUARD,
              "then":{"properties":{"items":{"items":{"oneOf":[
                {"required":["a"],"not":{"required":["b"]}},
                {"required":["absent"]}
              ]}}}},
              "else":{"properties":{"items":{"items":{"required":["b"],"not":{"required":["a"]}}}}}
            }
            """.Replace("GUARD", guard));

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.True(item.TryGetProperty("a", out _));
            Assert.False(item.TryGetProperty("b", out _));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_ConditionalRepair_QualifiesAncestorChoices(bool omitTag, bool nestedChild)
    {
        int tag = omitTag ? 0 : 1;
        var items = Enumerable.Range(0, 129).Select(_ => new
        {
            Tag = tag,
            A0 = (string?)null,
            B0 = (string?)null,
            A1 = (string?)null,
            B1 = (string?)null,
            Child = new { A0 = (string?)null, B0 = (string?)null, A1 = (string?)null, B1 = (string?)null },
        }).ToArray();
        JsonObject CreateBranch(int branchTag, string first, string second)
        {
            JsonArray alternatives = [
                new JsonObject { ["required"] = new JsonArray(first) },
                new JsonObject { ["required"] = new JsonArray(second) },
            ];
            JsonObject properties = new() { ["tag"] = new JsonObject { ["const"] = branchTag } };
            JsonObject branch = new() { ["properties"] = properties, ["required"] = new JsonArray("tag") };
            if (nestedChild)
            {
                properties["child"] = new JsonObject { ["oneOf"] = alternatives };
            }
            else
            {
                branch["oneOf"] = alternatives;
            }

            return branch;
        }

        JsonObject schema = new()
        {
            ["items"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(CreateBranch(1 - tag, "a0", "b0"), CreateBranch(tag, "a1", "b1")),
            },
        };
        McpServerTool tool = CreateStructuredOutputTestTool(
            items, schema.ToJsonString(), CreateStructuredOutputTestOptions(
                omitTag ? JsonIgnoreCondition.WhenWritingDefault : JsonIgnoreCondition.WhenWritingNull));

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item =>
        {
            Assert.Equal(tag, item.GetProperty("tag").GetInt32());
            JsonElement value = nestedChild ? item.GetProperty("child") : item;
            Assert.False(value.TryGetProperty("a0", out _));
            Assert.False(value.TryGetProperty("b0", out _));
            Assert.NotEqual(value.TryGetProperty("a1", out _), value.TryGetProperty("b1", out _));
        });
    }

    [Fact]
    public async Task StructuredOutput_ConditionalRepair_ReevaluatesInsideOneOfAfterRequiredRepair()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 0, A = (string?)null, B = (string?)null }).ToArray(),
            """
            {
              "items":{"oneOf":[
                {
                  "required":["tag"],
                  "if":{"properties":{"tag":{"const":1}}},
                  "then":{"required":["a"],"not":{"required":["b"]}},
                  "else":{"required":["b"],"not":{"required":["a"]}}
                },
                {"required":["absent"]}
              ]}
            }
            """, CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault));
        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Fact]
    public async Task StructuredOutput_ConditionalRepair_PreservesGuardsDuringRecursiveRepair()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Child = default(ConditionalRepairChild) }).ToArray(),
            """
            {
              "items":{
                "if":{"properties":{"child":{"required":["b"]}}},
                "then":{
                  "required":["child"],
                  "properties":{"child":{"oneOf":[
                    {"required":["a"],"not":{"required":["b"]}},
                    {"required":["b"],"not":{"required":["a"]}}
                  ]}}
                },
                "else":false
              }
            }
            """, CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault));

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item =>
        {
            Assert.True(item.GetProperty("child").TryGetProperty("b", out _));
            Assert.False(item.GetProperty("child").TryGetProperty("a", out _));
        });
    }

    [Theory]
    [InlineData(129, false, false)]
    [InlineData(129, false, true)]
    [InlineData(1024, false, true)]
    [InlineData(1024, true, true)]
    public async Task StructuredOutput_ConditionalRepair_ComposesOnlyMatchingBranches(int count, bool dictionary, bool mixed)
    {
        var items = Enumerable.Range(0, count)
            .Select(index => new { Tag = mixed ? index % 2 : 1, A = (string?)null, B = (string?)null }).ToArray();
        object output = dictionary
            ? items.Select((item, index) => (item, index)).ToDictionary(pair => $"item/~1/{pair.index}~", pair => pair.item)
            : items;
        string container = dictionary ? "\"additionalProperties\"" : "\"items\"";
        McpServerTool tool = CreateStructuredOutputTestTool(output, $$$"""
            {
              {{{container}}}:{
                "if":{"properties":{"tag":{"const":0} } },
                "then":{"required":["a"],"not":{"required":["b"]}},
                "else":{"required":["b"],"not":{"required":["a"]}}
              }
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        IEnumerable<JsonElement> repaired = dictionary
            ? result.EnumerateObject().Select(property => property.Value)
            : result.EnumerateArray();
        Assert.Equal(count, repaired.Count());
        foreach (JsonElement item in repaired)
        {
            bool zero = item.GetProperty("tag").GetInt32() == 0;
            Assert.Equal(JsonValueKind.Null, item.GetProperty(zero ? "a" : "b").ValueKind);
            Assert.False(item.TryGetProperty(zero ? "b" : "a", out _));
        }
    }

    [Fact]
    public async Task StructuredOutput_ConditionalRepair_ReevaluatesAfterRequiredRepair()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 0, A = (string?)null, B = (string?)null }).ToArray(),
            """
            {
              "items":{
                "required":["tag"],
                "if":{"properties":{"tag":{"const":1}}},
                "then":{"required":["a"],"not":{"required":["b"]}},
                "else":{"required":["b"],"not":{"required":["a"]}}
              }
            }
            """, CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault));

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item => Assert.True(item.TryGetProperty("b", out _)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_ConditionalRepair_PreservesAncestorGuardsThroughOneOf(bool conditionalInsideOneOf)
    {
        const string conditional = """
            {
              "if":{"properties":{"tag":{"const":0}}},
              "then":{"properties":{"child":{"oneOf":[{"$ref":"#/$defs/a"},{"required":["absent"]}]}}},
              "else":{"properties":{"child":{"oneOf":[{"$ref":"#/$defs/b"},{"required":["absent"]}]}}}
            }
            """;
        string entrySchema = conditionalInsideOneOf
            ? $$"""{"oneOf":[{"allOf":[{{conditional}}],"required":["tag"]},{"required":["absent"]}]}"""
            : conditional;
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 1, Child = new { A = (string?)null, B = (string?)null } }).ToArray(),
            $$$"""
            {
              "items":{{{entrySchema}}},
              "$defs":{
                "a":{"required":["a"],"not":{"required":["b"]}},
                "b":{"required":["b"],"not":{"required":["a"]}}
              }
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item =>
        {
            Assert.True(item.GetProperty("child").TryGetProperty("b", out _));
            Assert.False(item.GetProperty("child").TryGetProperty("a", out _));
        });
    }

    [Fact]
    public async Task StructuredOutput_ConditionalRepair_KeepsIndependentConditionsAtSamePath()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 1, A = (string?)null, B = (string?)null, C = (string?)null }).ToArray(),
            """
            {
              "items":{"allOf":[
                {"if":{"properties":{"tag":{"const":0}}},"then":{"$ref":"#/$defs/a"},"else":{"$ref":"#/$defs/b"}},
                {"if":{"properties":{"tag":{"const":1}}},"then":{"required":["c"]},"else":{"$ref":"#/$defs/a"}}
              ]},
              "$defs":{"a":{"required":["a"],"not":{"required":["b"]}},"b":{"required":["b"],"not":{"required":["a"]}}}
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item =>
        {
            Assert.True(item.TryGetProperty("b", out _));
            Assert.True(item.TryGetProperty("c", out _));
            Assert.False(item.TryGetProperty("a", out _));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_ConditionalRepair_UsesCallerReferenceScope(bool dynamicReference)
    {
        string condition = dynamicReference ? """{"$dynamicRef":"#guard"}""" : """{"$ref":"#/$defs/guard"}""";
        string anchor = dynamicReference ? "\"$dynamicAnchor\":\"guard\"," : "";
        int localTag = dynamicReference ? 1 : 0;
        McpServerTool tool = CreateStructuredOutputTestTool(
            Enumerable.Range(0, 129).Select(_ => new { Tag = 1, A = (string?)null, B = (string?)null }).ToArray(),
            $$$$"""
            {
              "$id":"https://example.test/conditional/root",
              "$defs":{
                "guard":{ {{{{anchor}}}} "properties":{"tag":{"const":0} } },
                "entry":{
                  "$id":"entry",
                  "$defs":{"guard":{ {{{{anchor}}}} "properties":{"tag":{"const":{{{{localTag}}}} } } } },
                  "if":{{{{condition}}}},
                  "then":{"required":["a"],"not":{"required":["b"]}},
                  "else":{"required":["b"],"not":{"required":["a"]}}
                }
              },
              "items":{"$ref":"entry"}
            }
            """);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        Assert.All(result.EnumerateArray(), item => Assert.True(item.TryGetProperty("b", out _)));
    }

    private struct ConditionalRepairChild
    {
        public string? A { get; set; }

        public string? B { get; set; }
    }
}
