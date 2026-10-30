using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.IO.Pipelines;
using System.Text.Json.Nodes;

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
services.AddMcpServer()
    .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
    .WithTools<AotTools>()
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

foreach (string name in new[] { "ForwardingEcho", "OverridingEcho" })
{
    var delegatedResult = await client.CallToolAsync(name, new Dictionary<string, object?> { ["arg"] = "Hello World" });
    if (delegatedResult.StructuredContent?.GetString() != "Echo: Hello World")
    {
        throw new Exception($"Unexpected delegated result for {name}.");
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

internal class ForwardingAotTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

internal sealed class OverridingAotTool(McpServerTool inner) : ForwardingAotTool(inner)
{
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
        base.InvokeAsync(request, cancellationToken);
}
