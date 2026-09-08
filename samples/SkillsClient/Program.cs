// Demonstrates consuming Agent Skills over MCP with the Skills extension (SEP-2640), from the host's side:
//
//   1. Connect over Streamable HTTP and check that the server declares the extension.
//   2. Enumerate the skills with skills/list (the client extension follows pagination for you).
//   3. Retrieve a single skill by URI with skills/get, as a host does for a URI mentioned in server instructions.
//   4. Read the skill's files with resources/read and verify each one against the manifest's digest and size.
//   5. Show that a file the manifest does not list is refused before any request is sent.
//
// Start the SkillsServer sample first (dotnet run --project samples/SkillsServer). Pass a different endpoint as
// the first argument to run against another server.

using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

var endpoint = new Uri(args.Length > 0 ? args[0] : "http://localhost:3001");

await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new()
{
    Name = "Skills Server",
    Endpoint = endpoint,
}));

Console.WriteLine($"Connected to {client.ServerInfo.Name} {client.ServerInfo.Version} at {endpoint} (protocol {client.NegotiatedProtocolVersion})");
Console.WriteLine();

// 1. Capability check. Clients issue skills/list and skills/get only after observing the declaration.
if (!client.SupportsSkills())
{
    Console.WriteLine("The server does not declare the io.modelcontextprotocol/skills extension.");
    return;
}

// 2. Enumerate. A listing may be empty or partial; a skill can always be fetched by URI.
Console.WriteLine("=== skills/list ===");
ListSkillsResult firstPage = await client.ListSkillsAsync(new ListSkillsRequestParams());
Console.WriteLine($"  caching hints: ttlMs={(firstPage.TimeToLive is { } ttl ? ttl.TotalMilliseconds.ToString() : "(none)")} cacheScope={firstPage.CacheScope?.ToString() ?? "(none)"}");

IList<Skill> skills = await client.ListSkillsAsync();
foreach (Skill listed in skills)
{
    string manifest = listed.Resources.IsDynamic
        ? "dynamic (no digests)"
        : $"{listed.Resources.Resources!.Count} file(s), {listed.Resources.Resources.Sum(r => r.Size)} bytes";
    Console.WriteLine($"  {listed.Uri}");
    Console.WriteLine($"    name: {listed.Name}");
    Console.WriteLine($"    description: {listed.Description}");
    Console.WriteLine($"    manifest: {manifest}");
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

// 4. Verified reads. ReadSkillResourceAsync issues resources/read and checks size and digest against the held
//    entry; a mismatch throws SkillVerificationException and the content must not be used.
Console.WriteLine("=== resources/read (verified) ===");
ReadResourceResult skillFile = await client.ReadSkillResourceAsync(skill, SkillUri);
Console.WriteLine($"  {SkillUri} verified:");
Console.WriteLine(Indent(((TextResourceContents)skillFile.Contents[0]).Text));

string supportingFileUri = skill.Resources.Resources.First(r => r.Uri != SkillUri).Uri;
ReadResourceResult supportingFile = await client.ReadSkillResourceAsync(skill, supportingFileUri);
Console.WriteLine($"  {supportingFileUri} verified ({((TextResourceContents)supportingFile.Contents[0]).Text.Length} chars)");
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
