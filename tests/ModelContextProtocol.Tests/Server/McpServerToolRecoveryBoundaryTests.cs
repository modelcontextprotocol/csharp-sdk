using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public partial class McpServerToolTests
{
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
    [JsonSerializable(typeof(object))]
    private partial class RecoveryBoundaryJsonContext : JsonSerializerContext;
}
