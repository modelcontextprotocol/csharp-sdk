using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_PrunesUnevaluatedRepairsAtControlCharacterPaths(bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions();
        if (sourceGenerated)
        {
            options.TypeInfoResolver = RecoveryBoundaryJsonContext.Default;
        }
        McpServerTool tool = CreateRecoveryBoundaryTool(
            new Dictionary<string, RecoveryBoundaryControlNameOutput>
            {
                ["evaluated\0path"] = new(),
                ["other"] = new(),
            }, """{"allOf":[{"properties":{"evaluated\u0000path":{"required":["real"]}}}],"unevaluatedProperties":{"required":["note"]}}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("evaluated\0path").GetProperty("real").ValueKind);
        Assert.False(result.GetProperty("evaluated\0path").TryGetProperty("note", out _));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("other").GetProperty("note").ValueKind);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotTraverseHiddenExtensionDataCollisions(bool sourceGenerated, bool defaultIgnored)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        IJsonTypeInfoResolver resolver = sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = resolver.WithAddedModifier(info =>
        {
            if (info.Type == typeof(RecoveryBoundaryFilteredParent) && !defaultIgnored)
            {
                info.Properties.Single(property => property.Name == "child").ShouldSerialize = static (_, _) => false;
            }
        });
        object output = defaultIgnored
            ? new RecoveryBoundaryDefaultParent { Child = new() { Count = 42 } }
            : new RecoveryBoundaryFilteredParent();
        McpServerTool tool = CreateRecoveryBoundaryTool(output, """
            {"properties":{"child":{"required":["count"],"properties":{"count":{"type":"integer"}}}}}
            """, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Empty(result.GetProperty("child").EnumerateObject());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_RecoveryBoundary_SupportsCachedReadonlyMetadata(bool sourceGenerated, bool record)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions();
        RecoveryBoundaryCachingResolver resolver = new(
            sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver());
        options.TypeInfoResolver = resolver;
        object output = record ? new RecoveryBoundaryNullableRecord() : new RecoveryBoundaryNullableOutput();
        McpServerTool tool = CreateStructuredOutputTestTool(output, """{"required":["note"]}""", options);
        JsonTypeInfo original = options.GetTypeInfo(output.GetType());
        Func<object, object?>? originalGetter = original.Properties.Single().Get;

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("note").ValueKind);
        Assert.True(original.IsReadOnly);
        Assert.Same(original, resolver.GetTypeInfo(original.Type, options));
        Assert.Same(originalGetter, original.Properties.Single().Get);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotRebuildWideContracts(bool cacheMetadata)
    {
        const int width = 32;
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        int ownerResolutions = 0;
        IJsonTypeInfoResolver resolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(typeInfo =>
        {
            if (typeInfo.Type == typeof(RecoveryBoundaryValue))
            {
                Interlocked.Increment(ref ownerResolutions);
                typeInfo.Properties.Clear();
                for (int i = 0; i < width; i++)
                {
                    JsonPropertyInfo property = typeInfo.CreateJsonPropertyInfo(typeof(int), $"count{i}");
                    property.Get = static value => ((RecoveryBoundaryValue)value).Count;
                    typeInfo.Properties.Add(property);
                }
            }
        });
        options.TypeInfoResolver = cacheMetadata ? new RecoveryBoundaryCachingResolver(resolver) : resolver;
        string required = string.Join(",", Enumerable.Range(0, width).Select(i => $"\"count{i}\""));
        McpServerTool tool = CreateStructuredOutputTestTool(
            new RecoveryBoundaryValue[8], $$$$"""{"items":{"required":[{{{{required}}}}]}}""", options);
        options.MakeReadOnly();
        JsonSerializer.SerializeToElement(new RecoveryBoundaryValue[8], options.GetTypeInfo(typeof(RecoveryBoundaryValue[])));
        int resolutionsBeforeRecovery = ownerResolutions;

        for (int invocation = 0; invocation < 2; invocation++)
        {
            JsonElement result = await InvokeStructuredOutputTestTool(tool);

            Assert.All(result.EnumerateArray(), item =>
            {
                Assert.Equal(width, item.EnumerateObject().Count());
                Assert.All(item.EnumerateObject(), property => Assert.Equal(0, property.Value.GetInt32()));
            });
            AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        }

        Assert.Equal(resolutionsBeforeRecovery, ownerResolutions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_KeepsConcurrentPlanValuesSeparate(bool nested)
    {
        using Barrier barrier = new(2);
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.ReferenceHandler = nested ? ReferenceHandler.IgnoreCycles : null;
        options.TypeInfoResolver = new RecoveryBoundaryCachingResolver(new DefaultJsonTypeInfoResolver());
        options.Converters.Add(new RecoveryBoundaryInvocationConverter(barrier, TestContext.Current.CancellationToken));
        McpServerTool[] tools = [CreateTool(1), CreateTool(2)];

        JsonElement[] results = await Task.WhenAll(tools.Select(tool =>
            Task.Run(() => InvokeStructuredOutputTestTool(tool), TestContext.Current.CancellationToken)));

        Assert.Equal(1, (nested ? results[0].GetProperty("child") : results[0]).GetProperty("value").GetInt32());
        Assert.Equal(2, (nested ? results[1].GetProperty("child") : results[1]).GetProperty("value").GetInt32());

        McpServerTool CreateTool(int value)
        {
            RecoveryBoundaryInvocationOutput output = new() { Value = new(value) };
            return nested
                ? CreateRecoveryBoundaryTool(new RecoveryBoundaryAncestorRoot { Child = output },
                    """{"properties":{"child":{"required":["value"]}}}""", options)
                : CreateStructuredOutputTestTool(output, """{"required":["value"]}""", options);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_RecoveryBoundary_PreservesOwnerCycleScope(bool valueTypeOwner, bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.Never);
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = new RecoveryBoundaryCachingResolver(
            sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver());
        object output = valueTypeOwner ? new RecoveryBoundaryCyclicStructOwner() : new RecoveryBoundaryCyclicOwner();
        RecoveryBoundaryCyclicValue value = new() { Owner = output };
        if (output is RecoveryBoundaryCyclicOwner referenceOwner)
        {
            referenceOwner.Missing = value;
        }
        else
        {
            typeof(RecoveryBoundaryCyclicStructOwner).GetProperty(nameof(RecoveryBoundaryCyclicStructOwner.Missing))!.SetValue(output, value);
        }
        McpServerTool tool = CreateRecoveryBoundaryTool(output, """{"required":["missing"]}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        JsonElement owner = result.GetProperty("missing").GetProperty("owner");
        if (valueTypeOwner)
        {
            Assert.Equal(JsonValueKind.Object, owner.ValueKind);
            Assert.Empty(owner.EnumerateObject());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, owner.ValueKind);
        }
    }

    public static IEnumerable<object[]> AncestorCycleCases()
    {
        foreach (string shape in new[] { "object", "nestedObject", "struct", "array", "arrayContainer", "dictionary", "dictionaryContainer", "rootArray", "rootDictionary", "extension" })
        {
            foreach (bool strict in new[] { false, true })
            {
                foreach (bool sourceGenerated in new[] { false, true })
                {
                    yield return [shape, strict, sourceGenerated];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AncestorCycleCases))]
    public async Task StructuredOutput_RecoveryBoundary_PreservesAncestorCycleScope(string shape, bool strict, bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.Never);
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = new RecoveryBoundaryCachingResolver(
            sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver());
        RecoveryBoundaryAncestorRoot root = new();
        RecoveryBoundaryCyclicOwner first = new(), second = new();
        RecoveryBoundaryCyclicOwner[] array = [first, second];
        Dictionary<string, RecoveryBoundaryCyclicOwner> dictionary = new()
        {
            ["first/~"] = first,
            ["second"] = second,
        };
        object output = root;
        object cycleTarget = root;
        root.Child = shape switch
        {
            "nestedObject" => new RecoveryBoundaryAncestorRoot { Child = first },
            "struct" => new RecoveryBoundaryStructAncestor { Child = first },
            "array" or "arrayContainer" => array,
            "dictionary" or "dictionaryContainer" => dictionary,
            "extension" => new RecoveryBoundaryAncestorExtension { Data = new() { ["entry/~"] = first } },
            _ => first,
        };
        if (shape is "arrayContainer" or "dictionaryContainer")
        {
            cycleTarget = root.Child!;
        }
        else if (shape is "rootArray" or "rootDictionary")
        {
            output = cycleTarget = shape == "rootArray" ? array : dictionary;
        }
        first.Missing = second.Missing = new() { Owner = cycleTarget };
        string ownerSchema = strict ? """{"type":"null"}""" : "{}";
        string itemSchema = $$$$"""
            {"type":"object","required":["missing"],"properties":{"missing":{
              "type":"object","required":["owner"],"properties":{"owner":{{{{ownerSchema}}}}}
            }}}
            """;
        string schema = (shape switch
        {
            "nestedObject" or "struct" => """{"properties":{"child":{"properties":{"child":ITEM}}}}""",
            "array" or "arrayContainer" => """{"properties":{"child":{"items":ITEM}}}""",
            "dictionary" or "dictionaryContainer" => """{"properties":{"child":{"additionalProperties":ITEM}}}""",
            "rootArray" => """{"items":ITEM}""",
            "rootDictionary" => """{"additionalProperties":ITEM}""",
            "extension" => """{"properties":{"child":{"properties":{"entry/~":ITEM}}}}""",
            _ => """{"properties":{"child":ITEM}}""",
        }).Replace("ITEM", itemSchema);
        McpServerTool tool = CreateRecoveryBoundaryTool(output, schema, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        JsonElement child = shape switch
        {
            "nestedObject" or "struct" => result.GetProperty("child").GetProperty("child"),
            "rootArray" or "rootDictionary" => result,
            "extension" => result.GetProperty("child").GetProperty("entry/~"),
            _ => result.GetProperty("child"),
        };
        if (shape is "array" or "arrayContainer" or "rootArray")
        {
            Assert.All(child.EnumerateArray(), AssertNullOwner);
        }
        else if (shape is "dictionary" or "dictionaryContainer" or "rootDictionary")
        {
            Assert.All(child.EnumerateObject(), property => AssertNullOwner(property.Value));
        }
        else
        {
            AssertNullOwner(child);
        }
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
        if (ReferenceEquals(root, output))
        {
            Assert.Equal(2, root.SerializingCalls);
            Assert.Equal(2, root.GetterCalls);
        }

        static void AssertNullOwner(JsonElement item) =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("missing").GetProperty("owner").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotTreatNonAncestorAsCycle(bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.Never);
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = new RecoveryBoundaryCachingResolver(
            sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver());
        RecoveryBoundaryCyclicOwner child = new()
        {
            Missing = new() { Owner = new RecoveryBoundaryNullableOutput { Note = "unrelated" } },
        };
        McpServerTool tool = CreateRecoveryBoundaryTool(new RecoveryBoundaryAncestorRoot { Child = child }, """
            {"properties":{"child":{"required":["missing"],"properties":{"missing":{
              "required":["owner"],"properties":{"owner":{
                "type":"object","required":["note"],"properties":{"note":{"const":"unrelated"}}
              }}
            }}}}}
            """, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal("unrelated", result.GetProperty("child").GetProperty("missing").GetProperty("owner").GetProperty("note").GetString());
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotTrackValueTypeAncestorBoxes(bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.Never);
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.TypeInfoResolver = new RecoveryBoundaryCachingResolver(
            sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver());
        RecoveryBoundaryCyclicOwner child = new();
        object boxedAncestor = new RecoveryBoundaryStructAncestor { Child = child };
        child.Missing = new() { Owner = boxedAncestor };
        McpServerTool tool = CreateRecoveryBoundaryTool(new RecoveryBoundaryAncestorRoot { Child = boxedAncestor }, """
            {"properties":{"child":{"properties":{"child":{"required":["missing"]}}}}}
            """, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        JsonElement owner = result.GetProperty("child").GetProperty("child").GetProperty("missing").GetProperty("owner");
        Assert.Equal(JsonValueKind.Object, owner.ValueKind);
        Assert.Equal(JsonValueKind.Null, owner.GetProperty("child").ValueKind);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task StructuredOutput_RecoveryBoundary_PreservesMemberConverterScope(bool factory, bool sourceGenerated, bool globalConverter)
    {
        RecoveryBoundaryDelegatingConverter converter = new();
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        if (globalConverter)
        {
            options.Converters.Insert(0, new RecoveryBoundaryGlobalIntConverter());
        }

        IJsonTypeInfoResolver resolver = sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = resolver.WithAddedModifier(typeInfo =>
        {
            if (typeInfo.Type == typeof(RecoveryBoundaryConvertedOutput))
            {
                foreach (JsonPropertyInfo property in typeInfo.Properties)
                {
                    property.CustomConverter = factory ? new RecoveryBoundaryConverterFactory(converter) : converter;
                }
            }
        });
        McpServerTool tool = CreateStructuredOutputTestTool(
            new RecoveryBoundaryConvertedOutput(),
            globalConverter
                ? """{"required":["count"],"properties":{"count":{"const":"global-0"},"existing":{"const":"global-5"}}}"""
                : """{"required":["count"],"properties":{"count":{"const":0},"existing":{"const":5}}}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.False(converter.RecursionDetected);
        Assert.False(converter.OptionsChanged);
        if (globalConverter)
        {
            Assert.Equal("global-0", result.GetProperty("count").GetString());
            Assert.Equal("global-5", result.GetProperty("existing").GetString());
        }
        else
        {
            Assert.Equal(0, result.GetProperty("count").GetInt32());
            Assert.Equal(5, result.GetProperty("existing").GetInt32());
        }
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_RecoveryBoundary_UsesSerializedDictionaryKeys(bool preserveReferences, bool reorder)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        if (preserveReferences)
        {
            options.ReferenceHandler = ReferenceHandler.Preserve;
        }

        RecoveryBoundaryDictionary output = new(reorder);
        McpServerTool tool = CreateRecoveryBoundaryTool(output, """
            {
              "required":["first/~","second"],
              "properties":{
                "first/~":{"required":["count"],"properties":{"count":{"type":"integer"}}},
                "second":{"required":["count"],"properties":{"count":{"type":"integer"}}}
              }
            }
            """, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(0, result.GetProperty("first/~").GetProperty("count").GetInt32());
        Assert.Equal(5, result.GetProperty("second").GetProperty("count").GetInt32());
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Fact]
    public async Task StructuredOutput_RecoveryBoundary_UsesDictionaryKeyConverter()
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.ReferenceHandler = ReferenceHandler.Preserve;
        options.Converters.Insert(0, new RecoveryBoundaryKeyConverter());
        McpServerTool tool = CreateRecoveryBoundaryTool(
            new Dictionary<RecoveryBoundaryKey, RecoveryBoundaryValue>
            {
                [new(1)] = default,
                [new(2)] = new() { Count = 5 },
            },
            """{"properties":{"key-1":{"required":["count"]},"key-2":{"required":["count"]}}}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(0, result.GetProperty("key-1").GetProperty("count").GetInt32());
        Assert.Equal(5, result.GetProperty("key-2").GetProperty("count").GetInt32());
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StructuredOutput_RecoveryBoundary_UsesNongenericDictionaryKeyDispatch(bool customKey, bool preserveReferences)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.Converters.Insert(0, new RecoveryBoundaryKeyConverter());
        if (preserveReferences)
        {
            options.ReferenceHandler = ReferenceHandler.Preserve;
        }

        Hashtable output = new()
        {
            [customKey ? new RecoveryBoundaryKey(1) : (object)1] = default(RecoveryBoundaryValue),
            [customKey ? new RecoveryBoundaryKey(2) : (object)2] = new RecoveryBoundaryValue { Count = 5 },
        };
        string first = customKey ? "key-1" : "1";
        string second = customKey ? "key-2" : "2";
        McpServerTool tool = CreateRecoveryBoundaryTool(output, $$$$"""
            {"properties":{"{{{{first}}}}":{"required":["count"]},"{{{{second}}}}":{"required":["count"]}}}
            """, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(0, result.GetProperty(first).GetProperty("count").GetInt32());
        Assert.Equal(5, result.GetProperty(second).GetProperty("count").GetInt32());
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("object")]
    [InlineData("array")]
    public async Task StructuredOutput_RecoveryBoundary_PreservesOutputWithCollidingDictionaryKeys(string shape)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        Dictionary<string, RecoveryBoundaryValue> dictionary = new()
        {
            ["First"] = default,
            ["first"] = new() { Count = 5 },
        };
        object output = shape switch
        {
            "object" => new { Child = dictionary },
            "array" => new[] { dictionary },
            _ => dictionary,
        };
        string schema = shape switch
        {
            "object" => """{"properties":{"child":{"required":["unavailable"]}}}""",
            "array" => """{"items":{"required":["unavailable"]}}""",
            _ => """{"required":["unavailable"]}""",
        };
        McpServerTool tool = CreateRecoveryBoundaryTool(output, schema, options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);
        result = shape switch
        {
            "object" => result.GetProperty("child"),
            "array" => result[0],
            _ => result,
        };

        Assert.Equal(2, result.EnumerateObject().Count());
        Assert.All(result.EnumerateObject(), property => Assert.Equal("first", property.Name));
    }

    [Theory]
    [InlineData("original", false)]
    [InlineData("original", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("wrapped", false)]
    [InlineData("wrapped", true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotRecoverExcludedReadonlyField(string provider, bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.IncludeFields = true;
        options.IgnoreReadOnlyFields = true;
        IJsonTypeInfoResolver resolver = sourceGenerated ? RecoveryBoundaryJsonContext.Default : new DefaultJsonTypeInfoResolver();
        options.TypeInfoResolver = resolver.WithAddedModifier(typeInfo =>
        {
            if (provider != "original")
            {
                foreach (JsonPropertyInfo property in typeInfo.Properties)
                {
                    property.AttributeProvider = provider == "missing" ? null : new RecoveryBoundaryAttributeProvider();
                }
            }
        });
        McpServerTool tool = CreateStructuredOutputTestTool(
            new RecoveryBoundaryReadonlyOutput(), """{"required":["count"]}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.False(result.TryGetProperty("count", out _));
    }

    [Fact]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotAddMembersToReferenceObjects()
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions();
        options.ReferenceHandler = ReferenceHandler.Preserve;
        RecoveryBoundaryNullableOutput child = new();
        McpServerTool tool = CreateRecoveryBoundaryTool(
            new { First = child, Second = child },
            """{"properties":{"first":{"required":["note"]},"second":{"required":["note"]}}}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        JsonProperty reference = Assert.Single(result.GetProperty("second").EnumerateObject());
        Assert.Equal("$ref", reference.Name);
        Assert.False(result.GetProperty("first").TryGetProperty("note", out _));
    }

    [Fact]
    public async Task StructuredOutput_RecoveryBoundary_TraversesPreservedCollectionValues()
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions();
        options.ReferenceHandler = ReferenceHandler.Preserve;
        McpServerTool tool = CreateRecoveryBoundaryTool(
            new List<RecoveryBoundaryNullableOutput> { new() },
            """{"properties":{"$values":{"items":{"required":["note"]}}}}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("$values")[0].GetProperty("note").ValueKind);
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    [Fact]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotIntroduceIndependentReferenceIds()
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        options.ReferenceHandler = ReferenceHandler.Preserve;
        McpServerTool tool = CreateRecoveryBoundaryTool(
            new { Existing = new RecoveryBoundaryNullableOutput(), Missing = default(RecoveryBoundaryReferenceValue) },
            """{"required":["missing"]}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.False(result.TryGetProperty("missing", out _));
        Assert.True(result.GetProperty("existing").TryGetProperty("$id", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredOutput_RecoveryBoundary_DoesNotRepeatOwnerCallbacksOrUnrelatedGetters(bool sourceGenerated)
    {
        JsonSerializerOptions options = CreateStructuredOutputTestOptions(JsonIgnoreCondition.WhenWritingDefault);
        if (sourceGenerated)
        {
            options.TypeInfoResolver = RecoveryBoundaryJsonContext.Default;
        }

        RecoveryBoundaryCallbackOutput output = new();
        McpServerTool tool = CreateStructuredOutputTestTool(output, """{"required":["count"]}""", options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(0, result.GetProperty("count").GetInt32());
        Assert.Equal(2, output.SerializingCalls);
        Assert.Equal(2, output.SerializedCalls);
        Assert.Equal(2, output.GetterCalls);
    }

    [Fact]
    public async Task StructuredOutput_RecoveryBoundary_PreservesCompatibleSourceGeneratedContracts()
    {
        McpServerTool tool = CreateStructuredOutputTestTool(
            new RecoveryBoundaryAttributedOutput(),
            """{"required":["count"],"properties":{"count":{"const":0}}}""",
            RecoveryBoundaryJsonContext.Default.Options);

        JsonElement result = await InvokeStructuredOutputTestTool(tool);

        Assert.Equal(0, result.GetProperty("count").GetInt32());
        AssertMatchesJsonSchema(tool.ProtocolTool.OutputSchema!.Value, result);
    }

    private sealed class RecoveryBoundaryConvertedOutput
    {
        public int Existing { get; set; } = 5;
        public int Count { get; set; }
    }

    private sealed class RecoveryBoundaryControlNameOutput
    {
        public string? Real { get; set; }
        public string? Note { get; set; }
    }

    private sealed class RecoveryBoundaryFilteredParent
    {
        public RecoveryBoundaryConvertedOutput Child { get; set; } = new() { Count = 42 };
        [JsonExtensionData]
        public Dictionary<string, object?> Data { get; set; } = new() { ["child"] = new Dictionary<string, object?>() };
    }

    private sealed class RecoveryBoundaryDefaultParent
    {
        public RecoveryBoundaryHiddenValue Child { get; set; }
        [JsonExtensionData]
        public Dictionary<string, object?> Data { get; set; } = new() { ["child"] = new Dictionary<string, object?>() };
    }

    private struct RecoveryBoundaryHiddenValue : IEquatable<RecoveryBoundaryHiddenValue>
    {
        public int Count { get; set; }
        public bool Equals(RecoveryBoundaryHiddenValue other) => true;
        public override bool Equals(object? obj) => obj is RecoveryBoundaryHiddenValue;
        public override int GetHashCode() => 0;
    }

    private sealed class RecoveryBoundaryDelegatingConverter : JsonConverter<int>
    {
        private bool _writing;
        private JsonSerializerOptions? _originalOptions;

        public bool RecursionDetected { get; private set; }
        public bool OptionsChanged { get; private set; }

        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            if (_writing)
            {
                RecursionDetected = true;
                throw new InvalidOperationException("Recursive member converter invocation.");
            }

            _originalOptions ??= options;
            OptionsChanged |= !ReferenceEquals(_originalOptions, options);
            _writing = true;
            try
            {
                JsonSerializer.Serialize(writer, value, options.GetTypeInfo(typeof(int)));
            }
            finally
            {
                _writing = false;
            }
        }
    }

    private sealed class RecoveryBoundaryConverterFactory(RecoveryBoundaryDelegatingConverter converter) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) => converter;
    }

    private sealed class RecoveryBoundaryGlobalIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"global-{value}");
    }

    private struct RecoveryBoundaryValue
    {
        public int Count { get; set; }
    }

    private sealed class RecoveryBoundaryDictionary(bool reorder) : IReadOnlyDictionary<string, RecoveryBoundaryValue>
    {
        private readonly Dictionary<string, RecoveryBoundaryValue> _entries = new()
        {
            ["First/~"] = default,
            ["Second"] = new() { Count = 5 },
        };
        private int _enumerations;

        public RecoveryBoundaryValue this[string key] => _entries[key];
        public IEnumerable<string> Keys => _entries.Keys;
        public IEnumerable<RecoveryBoundaryValue> Values => _entries.Values;
        public int Count => _entries.Count;
        public bool ContainsKey(string key) => _entries.ContainsKey(key);
        public bool TryGetValue(string key, out RecoveryBoundaryValue value) => _entries.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, RecoveryBoundaryValue>> GetEnumerator() =>
            (reorder && ++_enumerations % 2 == 0 ? _entries.Reverse() : _entries).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly record struct RecoveryBoundaryKey(int Value);

    private sealed class RecoveryBoundaryKeyConverter : JsonConverter<RecoveryBoundaryKey>
    {
        public override RecoveryBoundaryKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, RecoveryBoundaryKey value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"value-{value.Value}");
        public override void WriteAsPropertyName(Utf8JsonWriter writer, RecoveryBoundaryKey value, JsonSerializerOptions options) =>
            writer.WritePropertyName($"key-{value.Value}");
    }

    private sealed class RecoveryBoundaryReadonlyOutput
    {
        public readonly int Count = 0;
    }

    private sealed class RecoveryBoundaryAttributeProvider : ICustomAttributeProvider
    {
        public object[] GetCustomAttributes(bool inherit) => [];
        public object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
        public bool IsDefined(Type attributeType, bool inherit) => false;
    }

    private sealed class RecoveryBoundaryAttributedOutput
    {
        [JsonConverter(typeof(RecoveryBoundaryDelegatingConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Count { get; set; }
    }

    private sealed class RecoveryBoundaryNullableOutput
    {
        public string? Note { get; set; }
    }

    private sealed record RecoveryBoundaryNullableRecord(string? Note = null);

    private sealed class RecoveryBoundaryInvocationOutput
    {
        public RecoveryBoundaryInvocationValue Value { get; set; }
    }

    private readonly struct RecoveryBoundaryInvocationValue(int value) : IEquatable<RecoveryBoundaryInvocationValue>
    {
        public int Value { get; } = value;
        public bool Equals(RecoveryBoundaryInvocationValue other) => true;
        public override bool Equals(object? obj) => obj is RecoveryBoundaryInvocationValue;
        public override int GetHashCode() => 0;
    }

    private sealed class RecoveryBoundaryInvocationConverter(Barrier barrier, CancellationToken cancellationToken) : JsonConverter<RecoveryBoundaryInvocationValue>
    {
        public override RecoveryBoundaryInvocationValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, RecoveryBoundaryInvocationValue value, JsonSerializerOptions options)
        {
            if (!barrier.SignalAndWait(TestConstants.DefaultTimeout, cancellationToken))
            {
                throw new TimeoutException("Concurrent recovery did not enter both converters.");
            }
            writer.WriteNumberValue(value.Value);
        }
    }

    private sealed class RecoveryBoundaryCyclicOwner
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public RecoveryBoundaryCyclicValue Missing { get; set; }
    }

    private sealed class RecoveryBoundaryAncestorRoot : IJsonOnSerializing
    {
        public object? Child { get; set; }
        public int Existing { get { GetterCalls++; return 5; } }
        [JsonIgnore]
        public int GetterCalls { get; private set; }
        [JsonIgnore]
        public int SerializingCalls { get; private set; }
        public void OnSerializing() => SerializingCalls++;
    }

    private sealed class RecoveryBoundaryAncestorExtension
    {
        [JsonExtensionData]
        public Dictionary<string, object?> Data { get; set; } = new();
    }

    private struct RecoveryBoundaryStructAncestor
    {
        public RecoveryBoundaryCyclicOwner Child { get; set; }
    }

    private struct RecoveryBoundaryCyclicStructOwner
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public RecoveryBoundaryCyclicValue Missing { get; set; }
    }

    private struct RecoveryBoundaryCyclicValue : IEquatable<RecoveryBoundaryCyclicValue>
    {
        public object? Owner { get; set; }
        public bool Equals(RecoveryBoundaryCyclicValue other) => true;
        public override bool Equals(object? obj) => obj is RecoveryBoundaryCyclicValue;
        public override int GetHashCode() => 0;
    }

    private sealed class RecoveryBoundaryCachingResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, JsonSerializerOptions), JsonTypeInfo> _contracts = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
            _contracts.GetOrAdd((type, options), key => inner.GetTypeInfo(key.Item1, key.Item2)!);
    }

    private struct RecoveryBoundaryReferenceValue
    {
        public RecoveryBoundaryNullableOutput Child => new();
    }

    private sealed class RecoveryBoundaryCallbackOutput : IJsonOnSerializing, IJsonOnSerialized
    {
        public int Count { get; set; }
        public int Existing { get { GetterCalls++; return 5; } }
        [JsonIgnore]
        public int SerializingCalls { get; private set; }
        [JsonIgnore]
        public int SerializedCalls { get; private set; }
        [JsonIgnore]
        public int GetterCalls { get; private set; }
        public void OnSerializing() => SerializingCalls++;
        public void OnSerialized() => SerializedCalls++;
    }

    private static McpServerTool CreateRecoveryBoundaryTool(object value, string schema, JsonSerializerOptions options) =>
        McpServerTool.Create(new RecoveryBoundaryFunction(value, options), new()
        {
            UseStructuredContent = true,
            OutputSchema = JsonDocument.Parse(schema).RootElement,
        });

    private sealed class RecoveryBoundaryFunction(object value, JsonSerializerOptions options) : AIFunction
    {
        public override string Name => "tool";
        public override JsonElement JsonSchema { get; } = JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public override JsonSerializerOptions JsonSerializerOptions => options;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            new(value);
    }

    [JsonSourceGenerationOptions(IncludeFields = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(RecoveryBoundaryConvertedOutput))]
    [JsonSerializable(typeof(RecoveryBoundaryReadonlyOutput))]
    [JsonSerializable(typeof(RecoveryBoundaryCallbackOutput))]
    [JsonSerializable(typeof(RecoveryBoundaryAttributedOutput))]
    [JsonSerializable(typeof(RecoveryBoundaryNullableOutput))]
    [JsonSerializable(typeof(RecoveryBoundaryNullableRecord))]
    [JsonSerializable(typeof(Dictionary<string, RecoveryBoundaryControlNameOutput>))]
    [JsonSerializable(typeof(RecoveryBoundaryFilteredParent))]
    [JsonSerializable(typeof(RecoveryBoundaryDefaultParent))]
    [JsonSerializable(typeof(RecoveryBoundaryCyclicOwner))]
    [JsonSerializable(typeof(RecoveryBoundaryCyclicStructOwner))]
    [JsonSerializable(typeof(RecoveryBoundaryAncestorRoot))]
    [JsonSerializable(typeof(RecoveryBoundaryAncestorExtension))]
    [JsonSerializable(typeof(RecoveryBoundaryStructAncestor))]
    [JsonSerializable(typeof(RecoveryBoundaryCyclicOwner[]))]
    [JsonSerializable(typeof(Dictionary<string, RecoveryBoundaryCyclicOwner>))]
    [JsonSerializable(typeof(object))]
    private partial class RecoveryBoundaryJsonContext : JsonSerializerContext;
}
