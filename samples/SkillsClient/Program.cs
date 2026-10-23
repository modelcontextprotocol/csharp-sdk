// Demonstrates consuming Agent Skills over MCP with the Skills extension (SEP-2640), from the host's side:
//
//   1. Connect over Streamable HTTP and check that the server declares the extension.
//   2. Enumerate the skills with skills/list, page by page, showing each page's caching hints with its entries.
//   3. Retrieve a single skill by URI with skills/get, as a host does for a URI mentioned in server instructions.
//   4. Verify fetched bytes against the manifest, then parse SKILL.md frontmatter with YamlDotNet and compare it.
//   5. Show that a file the manifest does not list is refused before any request is sent.
//
// Start the SkillsServer sample first (dotnet run --project samples/SkillsServer). Pass a different endpoint as
// the first argument to run against another server.

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;
using YamlDotNet.Core;

var endpoint = new Uri(args.Length > 0 ? args[0] : "http://localhost:3001");

await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new()
{
    Name = "Skills Server",
    Endpoint = endpoint,
}));

// A server using server/discover may omit its optional identity, in which case ServerInfo is null.
string serverName = client.ServerInfo is { } serverInfo ? $"{serverInfo.Name} {serverInfo.Version}" : "the server";
Console.WriteLine($"Connected to {serverName} at {endpoint} (protocol {client.NegotiatedProtocolVersion})");
Console.WriteLine();

// 1. Capability check. Clients issue skills/list and skills/get only after observing the declaration.
if (!client.SupportsSkills())
{
    Console.WriteLine("The server does not declare the io.modelcontextprotocol/skills extension.");
    return;
}

// 2. Enumerate. A listing may be empty or partial; a skill can always be fetched by URI.
//    The per-page overload returns each page's caching hints with its entries. Only a missing cursor ends the
//    listing, so bound the number of pages and refuse a repeated cursor, as client.ListSkillsAsync() does when
//    the hints are not needed.
Console.WriteLine("=== skills/list ===");
const int MaxPages = 100;
var requestParams = new ListSkillsRequestParams();
var seenCursors = new HashSet<string>(StringComparer.Ordinal);
for (int pageNumber = 1; ; pageNumber++)
{
    if (pageNumber > MaxPages)
    {
        throw new McpException($"The skill listing exceeded the limit of {MaxPages} pages.");
    }

    ListSkillsResult page = await client.ListSkillsAsync(requestParams);
    Console.WriteLine($"  page {pageNumber} caching hints: ttlMs={(page.TimeToLive is { } ttl ? ttl.TotalMilliseconds.ToString() : "(none)")} cacheScope={page.CacheScope?.ToString() ?? "(none)"}");

    foreach (Skill listed in page.Skills)
    {
        string manifest = listed.Resources.IsDynamic
            ? "dynamic (no digests)"
            : $"{listed.Resources.Resources!.Count} file(s), {listed.Resources.Resources.Sum(r => r.Size)} bytes";
        Console.WriteLine($"  {listed.Uri}");
        Console.WriteLine($"    name: {listed.Name}");
        Console.WriteLine($"    description: {listed.Description}");
        Console.WriteLine($"    manifest: {manifest}");
    }

    if (page.NextCursor is null)
    {
        break;
    }

    if (!seenCursors.Add(page.NextCursor))
    {
        throw new McpException("The server repeated a skills/list pagination cursor.");
    }

    requestParams = new ListSkillsRequestParams { Cursor = page.NextCursor };
}

Console.WriteLine();

// 3. Retrieve one skill by URI. The server's instructions reference this one, so a host confirms it directly.
Console.WriteLine("=== skills/get ===");
Console.WriteLine($"  server instructions: {client.ServerInstructions}");
const string SkillUri = "skill://git-workflow/SKILL.md";
Skill skill = await client.GetSkillAsync(SkillUri);
Console.WriteLine($"  {skill.Uri} ({skill.Name})");
foreach (SkillResource file in skill.Resources.Resources!)
{
    Console.WriteLine($"    {file.Uri}  {file.Size,6} bytes  {file.Digest}");
}

Console.WriteLine();

// 4. ReadSkillResourceAsync checks manifest membership, URI, size and digest, not frontmatter.
//    The host must independently verify frontmatter before loading, regardless of the server's authoring path.
//    This sample parses it with YamlDotNet, a full YAML parser (see YamlFrontmatterReader.cs).
Console.WriteLine("=== resources/read (verified) ===");
ReadResourceResult skillFile = await client.ReadSkillResourceAsync(skill, SkillUri);
if (skillFile.Contents.Single(content => content.Uri == SkillUri) is not TextResourceContents skillText)
{
    throw new SkillVerificationException("This sample requires text SKILL.md contents for frontmatter verification.");
}

try
{
    var fetchedFrontmatter = new YamlFrontmatterReader().Read(skillText.Text);
    if (!JsonNode.DeepEquals(fetchedFrontmatter, skill.Frontmatter))
    {
        throw new SkillVerificationException("The fetched SKILL.md frontmatter differs from the held entry.");
    }
}
catch (Exception exception) when (exception is YamlException or FormatException)
{
    // Invalid YAML, or frontmatter without a JSON form, rejects this load. The server-provided JSON is never a
    // substitute for parsing the fetched file.
    throw new SkillVerificationException("The fetched SKILL.md frontmatter could not be verified.", exception);
}

Console.WriteLine($"  {SkillUri} manifest and frontmatter verified:");
Console.WriteLine(Indent(skillText.Text));

string supportingFileUri = skill.Resources.Resources.First(r => r.Uri != SkillUri).Uri;
ReadResourceResult supportingFile = await client.ReadSkillResourceAsync(skill, supportingFileUri);
string supportingSize = supportingFile.Contents.Single(content => content.Uri == supportingFileUri) switch
{
    TextResourceContents text => $"{text.Text.Length} chars",
    BlobResourceContents blob => $"{blob.DecodedData.Length} bytes",
    _ => "content of an unknown kind",
};
Console.WriteLine($"  {supportingFileUri} verified ({supportingSize})");
Console.WriteLine();

// 5. An unlisted file is a change to the skill. While acting on the held entry, the host must not read it.
Console.WriteLine("=== unlisted file ===");
try
{
    await client.ReadSkillResourceAsync(skill, "skill://git-workflow/references/UNLISTED.md");
}
catch (SkillVerificationException e)
{
    Console.WriteLine($"  refused: {e.Message}");
}

static string Indent(string text) =>
    string.Join(Environment.NewLine, text.TrimEnd().Split('\n').Select(line => "    | " + line.TrimEnd('\r')));
