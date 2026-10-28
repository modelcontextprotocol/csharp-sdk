using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

Pipe clientToServerPipe = new(), serverToClientPipe = new();

var authoredSkill = McpServerSkill.Create(
    "skill://aot/SKILL.md",
    [McpServerSkillFile.FromText("SKILL.md", "---\nname: aot\ndescription:\n  An AOT-published skill.\nmetadata:\n  note: >\n    First\n    \tCode\n    Last\n---\n")]);
var expectedMeta = JsonNode.Parse("""{"example.com/aot":{"values":["native",42,true]}}""")!.AsObject();
var entry = authoredSkill.ProtocolSkill;
entry.Meta = (JsonObject)expectedMeta.DeepClone();
var catalog = new InMemoryMcpSkillCatalog([entry]);
entry.Meta["example.com/aot"]!["values"]!.AsArray().Clear();

var services = new ServiceCollection();
JsonSerializerOptions structuredOptions = new(McpJsonUtilities.DefaultOptions)
{
    IgnoreReadOnlyProperties = true,
    TypeInfoResolver = new AotCachingResolver(JsonTypeInfoResolver.Combine(AotJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver)
        .WithAddedModifier(typeInfo =>
        {
            if (typeInfo.Type == typeof(AotBaseOutput))
            {
                typeInfo.Properties.Single(property => property.Name == "flag").ShouldSerialize = static (_, _) => false;
            }
        })),
};
McpServerTool structuredTool = McpServerTool.Create(
    () => new AotStructuredOutput(),
    new()
    {
        Name = "RequiredDefaults",
        UseStructuredContent = true,
        SerializerOptions = structuredOptions,
        OutputSchema = JsonDocument.Parse("""
            {
              "$id":"https://example.test/aot",
              "type":"object",
              "required":["id","note","value","items","genericItems","dynamicItems","unions","numbers","extension","contract","nullable","delegatingCount"],
              "anyOf":[{"required":["count"]},{"required":["note"]}],
              "$defs":{"child":{"$id":"child","type":"object","required":["count"],"properties":{"count":{"const":0}}}},
              "properties":{
                "id":{"type":"null"},"note":{"type":"null"},"value":{"const":"converted"},
                "items":{"type":"object","additionalProperties":{"$ref":"child"}},
                "genericItems":{"type":"object","additionalProperties":{"$ref":"child"}},
                "dynamicItems":{"type":"object","additionalProperties":{"$ref":"child"}},
                "numbers":{"required":["count"],"properties":{"count":{"const":"0"}}},
                "extension":{"properties":{"child/~":{"required":["note"]}}},
                "contract":{"oneOf":[{"required":["flag"]},{"required":["note"]}]},
                "nullable":{"required":["note"]},
                "delegatingCount":{"const":0},
                "unions":{"type":"array","items":{"oneOf":[
                  {"properties":{"tag":{"const":"0"},"child0":{"required":["note"]}},"required":["tag","child0"],"additionalProperties":false},
                  {"properties":{"tag":{"const":"1"},"child1":{"required":["note"]}},"required":["tag","child1"],"additionalProperties":false}
                ]}}
              }
            }
            """).RootElement,
    });
JsonSerializerOptions preservedOptions = new(structuredOptions) { ReferenceHandler = ReferenceHandler.Preserve };
McpServerTool preservedTool = McpServerTool.Create(
    new AotPreservedFunction(preservedOptions),
    new()
    {
        UseStructuredContent = true,
        OutputSchema = JsonDocument.Parse("""
            {
              "required":["entry/1~","entry/2~"],
              "properties":{
                "entry/1~":{"required":["count"],"properties":{"count":{"type":"integer"}}},
                "entry/2~":{"required":["count"],"properties":{"count":{"type":"integer"}}}
              }
            }
            """).RootElement,
    });
McpServerTool preservedNongenericTool = McpServerTool.Create(
    new AotPreservedFunction(preservedOptions, nongeneric: true),
    new()
    {
        UseStructuredContent = true,
        OutputSchema = preservedTool.ProtocolTool.OutputSchema,
    });
McpServerTool ancestorTool = McpServerTool.Create(
    new AotAncestorFunction(new(structuredOptions)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    }),
    new()
    {
        UseStructuredContent = true,
        OutputSchema = JsonDocument.Parse("""
            {
              "type":"object",
              "$defs":{"node":{"required":["missing"],"properties":{"missing":{
                "required":["owner"],"properties":{"owner":{"type":"null"}}
              }}}},
              "properties":{
                "child":{"$ref":"#/$defs/node"},
                "items":{"items":{"$ref":"#/$defs/node"}},
                "entries":{"additionalProperties":{"$ref":"#/$defs/node"}}
              }
            }
            """).RootElement,
    });
services.AddMcpServer()
    .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
    .WithTools<AotTools>()
    .WithTools([new ForwardingAotTool(structuredTool), preservedTool, preservedNongenericTool, ancestorTool])
    .WithTools(
    [
        new ForwardingAotTool(McpServerTool.Create(AotTools.Echo, new() { Name = "ForwardingEcho", UseStructuredContent = true })),
        new OverridingAotTool(McpServerTool.Create(AotTools.Echo, new() { Name = "OverridingEcho", UseStructuredContent = true })),
    ])
    .WithMcpApps()
    .WithSkills(catalog)
    .WithResources(authoredSkill.Resources);

await using var serviceProvider = services.BuildServiceProvider();
var server = serviceProvider.GetRequiredService<McpServer>();
_ = server.RunAsync();

// Connect a client using a stream-based transport over the same in-memory pipe.
await using McpClient client = await TestMcpClient.CreateAsync(
    new StreamClientTransport(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream()));

// List all tools.
var tools = await client.ListToolsAsync();
var echo = tools.FirstOrDefault(t => t.Name == "Echo");
if (echo is null)
{
    throw new Exception("Expected the Echo tool.");
}

var ui = echo.ProtocolTool.Meta?["ui"]?.AsObject();
if (ui?["resourceUri"]?.GetValue<string>() != "ui://aot/echo")
{
    throw new Exception($"Unexpected app UI metadata: {ui}");
}

var result = await echo.InvokeAsync(new() { ["arg"] = "Hello World" });
if (result is null || !result.ToString()!.Contains("Echo: Hello World"))
{
    throw new Exception($"Unexpected result: {result}");
}

var structuredResult = await client.CallToolAsync("RequiredDefaults");
if (structuredResult.StructuredContent is not { } structured ||
    structured.GetProperty("id").ValueKind != JsonValueKind.Null ||
    structured.GetProperty("note").ValueKind != JsonValueKind.Null ||
    structured.GetProperty("value").GetString() != "converted" ||
    structured.GetProperty("items").GetProperty("42").GetProperty("count").GetInt32() != 0 ||
    structured.GetProperty("genericItems").GetProperty("42").GetProperty("count").GetInt32() != 0 ||
    structured.GetProperty("dynamicItems").GetProperty("entry").GetProperty("count").GetInt32() != 0 ||
    structured.GetProperty("numbers").GetProperty("count").GetString() != "0" ||
    structured.GetProperty("extension").GetProperty("child/~").GetProperty("note").ValueKind != JsonValueKind.Null ||
    structured.GetProperty("contract").GetProperty("note").ValueKind != JsonValueKind.Null ||
    structured.GetProperty("contract").TryGetProperty("flag", out _) ||
    structured.GetProperty("nullable").GetProperty("note").ValueKind != JsonValueKind.Null ||
    structured.GetProperty("delegatingCount").GetInt32() != 0 ||
    structured.TryGetProperty("count", out _))
{
    throw new Exception($"Unexpected structured output: {structuredResult.StructuredContent}");
}

foreach (JsonElement item in structured.GetProperty("unions").EnumerateArray())
{
    if (item.GetProperty("child0").GetProperty("note").ValueKind != JsonValueKind.Null ||
        item.TryGetProperty("child1", out _))
    {
        throw new Exception($"Unexpected union output: {item}");
    }
}

foreach (string name in new[] { "PreservedDictionary", "PreservedNongenericDictionary" })
{
    var preservedResult = await client.CallToolAsync(name);
    if (preservedResult.StructuredContent is not { } preserved ||
        preserved.GetProperty("entry/1~").GetProperty("count").GetInt32() != 0 ||
        preserved.GetProperty("entry/2~").GetProperty("count").GetInt32() != 5)
    {
        throw new Exception($"Unexpected preserved dictionary output: {preservedResult.StructuredContent}");
    }
}

foreach (string name in new[] { "ForwardingEcho", "OverridingEcho" })
{
    var delegatedResult = await client.CallToolAsync(name, new Dictionary<string, object?> { ["arg"] = "Hello World" });
    if (delegatedResult.StructuredContent?.GetString() != "Echo: Hello World")
    {
        throw new Exception($"Unexpected delegated result for {name}.");
    }
}

var ancestorResult = await client.CallToolAsync("AncestorCycles");
JsonElement ancestor = ancestorResult.StructuredContent!.Value;
foreach (JsonElement node in ancestor.GetProperty("items").EnumerateArray()
    .Concat(ancestor.GetProperty("entries").EnumerateObject().Select(property => property.Value))
    .Prepend(ancestor.GetProperty("child")))
{
    if (node.GetProperty("missing").GetProperty("owner").ValueKind != JsonValueKind.Null)
    {
        throw new Exception($"Unexpected ancestor cycle output: {ancestor}");
    }
}

// List, get, and verify a skill.
var skills = await client.ListSkillsAsync();
if (skills.Count != 1 || skills[0].Name != "aot")
{
    throw new Exception($"Unexpected skills listing: {skills.Count} entries.");
}

if (!JsonNode.DeepEquals(expectedMeta, skills[0].Meta))
{
    throw new Exception("Unexpected skill entry metadata in the listing.");
}

var skillResult = await client.GetSkillAsync(new GetSkillRequestParams { Uri = "skill://aot/SKILL.md" });
if (skillResult.ResultType != "complete" ||
    skillResult.TimeToLive != TimeSpan.Zero ||
    skillResult.CacheScope != CacheScope.Private)
{
    throw new Exception("Unexpected skill cache hints.");
}

var skill = skillResult.Skill;
if (!JsonNode.DeepEquals(expectedMeta, skill.Meta))
{
    throw new Exception("Unexpected skill entry metadata in the lookup.");
}

if (skill.Description != "An AOT-published skill." ||
    skill.Frontmatter["metadata"]?["note"]?.GetValue<string>() != "First\n\tCode\nLast\n")
{
    throw new Exception("Unexpected skill frontmatter.");
}

var skillFile = await client.ReadSkillResourceAsync(skill, skill.Uri);
if (skillFile.Contents is not [TextResourceContents { Text: var skillText }] || !skillText.Contains("name: aot"))
{
    throw new Exception("Unexpected skill content.");
}

Console.WriteLine("Success!");

[McpServerToolType]
internal sealed class AotTools
{
    [McpServerTool(Name = "Echo", UseStructuredContent = true)]
    [McpAppUi(ResourceUri = "ui://aot/echo")]
    public static string Echo(string arg) => $"Echo: {arg}";
}

internal sealed class AotStructuredOutput
{
    public int Count => throw new InvalidOperationException("Excluded getter was invoked.");

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public RequestId Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(AotElementConverter))]
    public JsonElement Value { get; init; }

    public ReadOnlyDictionary<int, AotStructuredChild> Items { get; init; } =
        new(new Dictionary<int, AotStructuredChild> { [42] = default });

    public AotGenericOnlyDictionary<int, AotStructuredChild> GenericItems { get; init; } =
        new(new Dictionary<int, AotStructuredChild> { [42] = default });

    public ExpandoObject DynamicItems { get; init; } = CreateDynamicItems();

    public AotUnionOutput[] Unions { get; init; } = Enumerable.Range(0, 5).Select(_ => new AotUnionOutput()).ToArray();

    public AotNumberOutput Numbers { get; init; } = new();
    public AotExtensionOutput Extension { get; init; } = new();
    public AotBaseOutput Contract { get; init; } = new AotDerivedOutput();
    public AotNullableChild? Nullable { get; init; } = new AotNullableChild();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonConverter(typeof(AotDelegatingIntConverter))]
    public int DelegatingCount { get; init; }

    [JsonConverter(typeof(AotRejectNullConverter))]
    public string? Optional { get; init; }

    private static ExpandoObject CreateDynamicItems()
    {
        ExpandoObject items = new();
        ((IDictionary<string, object?>)items)["entry"] = default(AotStructuredChild);
        return items;
    }
}

[JsonNumberHandling(JsonNumberHandling.WriteAsString)]
internal sealed class AotNumberOutput
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Count { get; init; }
}

internal sealed class AotExtensionOutput
{
    [JsonExtensionData]
    public Dictionary<string, object?> Data { get; set; } = new() { ["child/~"] = new AotNullableChild() };
}

internal class AotBaseOutput
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Flag { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

internal sealed class AotDerivedOutput : AotBaseOutput;

internal sealed class AotRejectNullConverter : JsonConverter<string>
{
    public override bool HandleNull => true;
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        throw new ArgumentNullException(nameof(value));
}

internal sealed class AotUnionOutput
{
    public string Tag { get; init; } = "0";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AotNullableChild Child0 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AotNullableChild Child1 { get; init; }
}

internal struct AotNullableChild
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

internal sealed class AotGenericOnlyDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> entries) : IReadOnlyDictionary<TKey, TValue>
{
    public TValue this[TKey key] => entries[key];
    public IEnumerable<TKey> Keys => entries.Keys;
    public IEnumerable<TValue> Values => entries.Values;
    public int Count => entries.Count;
    public bool ContainsKey(TKey key) => entries.ContainsKey(key);
    public bool TryGetValue(TKey key, out TValue value) => entries.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => entries.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal struct AotStructuredChild
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Count { get; init; }
}

internal sealed class AotElementConverter : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) =>
        writer.WriteStringValue("converted");
}

internal sealed class AotDelegatingIntConverter : JsonConverter<int>
{
    private bool _writing;
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        if (_writing)
        {
            throw new InvalidOperationException("Recursive member converter invocation.");
        }

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

[JsonConverter(typeof(AotDictionaryKeyConverter))]
internal readonly record struct AotDictionaryKey(int Value);

internal sealed class AotDictionaryKeyConverter : JsonConverter<AotDictionaryKey>
{
    public override AotDictionaryKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();
    public override void Write(Utf8JsonWriter writer, AotDictionaryKey value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
    public override void WriteAsPropertyName(Utf8JsonWriter writer, AotDictionaryKey value, JsonSerializerOptions options) =>
        writer.WritePropertyName($"entry/{value.Value}~");
}

internal sealed class AotPreservedFunction(JsonSerializerOptions options, bool nongeneric = false) : AIFunction
{
    private readonly object _value = nongeneric
        ? new Hashtable
        {
            [new AotDictionaryKey(1)] = default(AotStructuredChild),
            [new AotDictionaryKey(2)] = new AotStructuredChild { Count = 5 },
        }
        : new Dictionary<AotDictionaryKey, AotStructuredChild>
        {
            [new(1)] = default,
            [new(2)] = new() { Count = 5 },
        };
    public override string Name => nongeneric ? "PreservedNongenericDictionary" : "PreservedDictionary";
    public override JsonElement JsonSchema { get; } = JsonDocument.Parse("""{"type":"object"}""").RootElement;
    public override JsonSerializerOptions JsonSerializerOptions => options;
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        new(_value);
}

internal sealed class AotAncestorRoot
{
    public AotAncestorOwner Child { get; set; } = new();
    public AotAncestorOwner[] Items { get; set; } = [new(), new()];
    public Dictionary<string, AotAncestorOwner> Entries { get; set; } = new()
    {
        ["root"] = new(),
        ["container"] = new(),
    };
}

internal sealed class AotAncestorOwner
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AotAncestorValue Missing { get; set; }
}

internal struct AotAncestorValue : IEquatable<AotAncestorValue>
{
    public object? Owner { get; set; }
    public bool Equals(AotAncestorValue other) => true;
    public override bool Equals(object? obj) => obj is AotAncestorValue;
    public override int GetHashCode() => 0;
}

internal sealed class AotAncestorFunction(JsonSerializerOptions options) : AIFunction
{
    private readonly AotAncestorRoot _value = CreateValue();
    public override string Name => "AncestorCycles";
    public override JsonElement JsonSchema { get; } = JsonDocument.Parse("""{"type":"object"}""").RootElement;
    public override JsonSerializerOptions JsonSerializerOptions => options;
    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        new(_value);

    private static AotAncestorRoot CreateValue()
    {
        AotAncestorRoot root = new();
        root.Child.Missing = new() { Owner = root };
        root.Items[0].Missing = new() { Owner = root };
        root.Items[1].Missing = new() { Owner = root.Items };
        root.Entries["root"].Missing = new() { Owner = root };
        root.Entries["container"].Missing = new() { Owner = root.Entries };
        return root;
    }
}

internal sealed class AotCachingResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, JsonSerializerOptions), JsonTypeInfo> _contracts = new();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) =>
        _contracts.GetOrAdd((type, options), key => inner.GetTypeInfo(key.Item1, key.Item2)!);
}

[JsonSerializable(typeof(AotStructuredOutput))]
[JsonSerializable(typeof(AotDerivedOutput))]
[JsonSerializable(typeof(Dictionary<AotDictionaryKey, AotStructuredChild>))]
[JsonSerializable(typeof(Hashtable))]
[JsonSerializable(typeof(AotAncestorRoot))]
internal partial class AotJsonContext : JsonSerializerContext;

internal class ForwardingAotTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

internal sealed class OverridingAotTool(McpServerTool inner) : ForwardingAotTool(inner)
{
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
        base.InvokeAsync(request, cancellationToken);
}
