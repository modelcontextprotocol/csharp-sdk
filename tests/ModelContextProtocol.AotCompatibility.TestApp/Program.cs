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
var catalogTracker = new AotCatalogTracker();
services.AddSingleton(catalog);
services.AddSingleton(catalogTracker);
services.AddScoped<AotCatalogDependency>();
services.AddMcpServer()
    .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
    .WithTools<AotTools>()
    .WithMcpApps()
    .WithSkills<AotSkillCatalog>()
    .WithResources(authoredSkill.Resources);

await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
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

if (catalogTracker.Created != 2 || catalogTracker.Disposed != 2)
{
    throw new Exception($"Unexpected catalog scope lifetimes: {catalogTracker.Created} created, {catalogTracker.Disposed} disposed.");
}

Console.WriteLine("Success!");

[McpServerToolType]
internal sealed class AotTools
{
    [McpServerTool(Name = "Echo")]
    [McpAppUi(ResourceUri = "ui://aot/echo")]
    public static string Echo(string arg) => $"Echo: {arg}";
}

internal sealed class AotCatalogTracker
{
    public int Created;
    public int Disposed;
}

internal sealed class AotCatalogDependency : IAsyncDisposable
{
    private readonly AotCatalogTracker _tracker;

    public AotCatalogDependency(AotCatalogTracker tracker)
    {
        _tracker = tracker;
        Interlocked.Increment(ref tracker.Created);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Interlocked.Increment(ref _tracker.Disposed);
    }
}

internal sealed class AotSkillCatalog(InMemoryMcpSkillCatalog catalog, AotCatalogDependency dependency) : IMcpSkillCatalog
{
    public ValueTask<McpSkillPage> ListAsync(string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken)
    {
        ValidateRequestServices(context);
        return catalog.ListAsync(cursor, context, cancellationToken);
    }

    public ValueTask<Skill?> GetAsync(string uri, McpSkillRequestContext context, CancellationToken cancellationToken)
    {
        ValidateRequestServices(context);
        return catalog.GetAsync(uri, context, cancellationToken);
    }

    private void ValidateRequestServices(McpSkillRequestContext context)
    {
        if (!ReferenceEquals(dependency, context.Services?.GetRequiredService<AotCatalogDependency>()) ||
            ReferenceEquals(context.Services, context.Server.Services))
        {
            throw new Exception("The catalog did not receive request-scoped services.");
        }
    }
}
