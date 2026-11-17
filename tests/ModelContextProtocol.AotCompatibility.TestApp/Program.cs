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
const string FallbackUri = "skill://aot-fallback/SKILL.md";

var expectedMeta = JsonNode.Parse("""{"example.com/aot":{"values":["native",42,true]}}""")!.AsObject();
var metadata = new List<object> { "local-aot-metadata" };
var createOptions = new McpServerSkillPackageCreateOptions
{
    Uri = "skill://aot/SKILL.md",
    Metadata = metadata,
    Meta = expectedMeta,
};
var package = McpServerSkillPackage.Create(
    [McpServerSkillFile.FromText("SKILL.md", "---\nname: aot\ndescription:\n  An AOT-published skill.\nmetadata:\n  note: >\n    First\n    \tCode\n    Last\n---\n")], createOptions);
var primitive = package.Skill;
var services = new ServiceCollection();
var catalogTracker = new AotCatalogTracker();
services.AddSingleton(catalogTracker);
services.AddScoped<AotCatalogDependency>();
services.AddMcpServer()
    .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
    .WithTools<AotTools>()
    .WithTools(
    [
        new ForwardingAotTool(McpServerTool.Create(AotTools.Echo, new() { Name = "ForwardingEcho", UseStructuredContent = true })),
        new OverridingAotTool(McpServerTool.Create(AotTools.Echo, new() { Name = "OverridingEcho", UseStructuredContent = true })),
    ])
    .WithMcpApps()
    .WithSkillPackages([package]).WithSkills(options =>
    {
        options.ListSkillsHandler = (_, _) => new(new ListSkillsResult());
        options.GetSkillHandler = (context, _) =>
        {
            if (context.MatchedPrimitive is not null || context.Params.Uri != FallbackUri)
            {
                throw new Exception("Unexpected custom get fallback context.");
            }

            var fallback = new Skill
            {
                Uri = FallbackUri,
                Frontmatter = new JsonObject { ["name"] = "aot-fallback", ["description"] = "An AOT fallback skill." },
                Resources = SkillResources.Dynamic,
                Meta = expectedMeta,
            };
            return new(new GetSkillResult { Skill = fallback, TimeToLive = TimeSpan.FromMinutes(1), CacheScope = CacheScope.Public });
        };

        options.GetSkillFilters.Add(next => (context, cancellationToken) =>
        {
            _ = context.Services!.GetRequiredService<AotCatalogDependency>();
            if (context.MatchedPrimitive?.Id == primitive.Id &&
                (!ReferenceEquals(context.MatchedPrimitive, primitive) || primitive.Metadata is not ["local-aot-metadata"]))
            {
                throw new Exception("The get filter did not receive the matched primitive metadata.");
            }

            return next(context, cancellationToken);
        });
    });

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

var skillResult = await client.GetSkillAsync(new GetSkillRequestParams { Uri = "SKILL://AOT/SKILL.md" });
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

var fallbackResult = await client.GetSkillAsync(new GetSkillRequestParams { Uri = FallbackUri });
if (fallbackResult.Skill.Uri != FallbackUri ||
    fallbackResult.Skill.Name != "aot-fallback" ||
    !JsonNode.DeepEquals(expectedMeta, fallbackResult.Skill.Meta) ||
    fallbackResult.ResultType != "complete" ||
    fallbackResult.TimeToLive != TimeSpan.FromMinutes(1) ||
    fallbackResult.CacheScope != CacheScope.Public)
{
    throw new Exception("Unexpected custom get fallback result.");
}

if (catalogTracker.Created != 2 || catalogTracker.Disposed != 2)
{
    throw new Exception("Unexpected request-service lifetimes for registered get and custom fallback.");
}

Console.WriteLine("Success!");

[McpServerToolType]
internal sealed class AotTools
{
    [McpServerTool(Name = "Echo", UseStructuredContent = true)]
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

internal class ForwardingAotTool(McpServerTool inner) : DelegatingMcpServerTool(inner);

internal sealed class OverridingAotTool(McpServerTool inner) : ForwardingAotTool(inner)
{
    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
        base.InvokeAsync(request, cancellationToken);
}
