using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
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
    TypeInfoResolver = JsonTypeInfoResolver.Combine(AotJsonContext.Default, McpJsonUtilities.DefaultOptions.TypeInfoResolver),
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
              "required":["id","note","value","items","genericItems","dynamicItems","unions"],
              "anyOf":[{"required":["count"]},{"required":["note"]}],
              "$defs":{"child":{"$id":"child","type":"object","required":["count"],"properties":{"count":{"const":0}}}},
              "properties":{
                "id":{"type":"null"},"note":{"type":"null"},"value":{"const":"converted"},
                "items":{"type":"object","additionalProperties":{"$ref":"child"}},
                "genericItems":{"type":"object","additionalProperties":{"$ref":"child"}},
                "dynamicItems":{"type":"object","additionalProperties":{"$ref":"child"}},
                "unions":{"type":"array","items":{"oneOf":[
                  {"properties":{"tag":{"const":"0"},"child0":{"required":["note"]}},"required":["tag","child0"],"additionalProperties":false},
                  {"properties":{"tag":{"const":"1"},"child1":{"required":["note"]}},"required":["tag","child1"],"additionalProperties":false}
                ]}}
              }
            }
            """).RootElement,
    });
services.AddMcpServer()
    .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
    .WithTools<AotTools>()
    .WithTools([structuredTool])
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
    [McpServerTool(Name = "Echo")]
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

    private static ExpandoObject CreateDynamicItems()
    {
        ExpandoObject items = new();
        ((IDictionary<string, object?>)items)["entry"] = default(AotStructuredChild);
        return items;
    }
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

[JsonSerializable(typeof(AotStructuredOutput))]
internal partial class AotJsonContext : JsonSerializerContext;
