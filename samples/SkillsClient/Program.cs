// Demonstrates consuming Agent Skills over MCP with the Skills extension (SEP-2640), from the host's side:
//
//   1. Connect over Streamable HTTP and check that the server declares the extension.
//   2. Enumerate the skills with skills/list, page by page, showing each page's caching hints with its entries.
//   3. Retrieve a single skill by URI with skills/get, as a host does for a URI mentioned in server instructions.
//   4. Verify fetched bytes against the manifest, then parse SKILL.md frontmatter with YamlDotNet and compare it.
//   5. Show that a file the manifest does not list is refused before any request is sent.
//
// Start the SkillsServer sample first (dotnet run --project samples/SkillsServer). Pass a different endpoint as
// the first argument to run against another instance of this sample server.

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

var endpoint = new Uri(args.Length > 0 ? args[0] : "http://localhost:3001");

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    await RunAsync(endpoint, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Cancelled.");
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

static async Task RunAsync(Uri endpoint, CancellationToken cancellationToken)
{
    await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new()
    {
        Name = "Skills Server",
        Endpoint = endpoint,
    }), cancellationToken: cancellationToken);

    // A server using server/discover may omit its optional identity, in which case ServerInfo is null.
    string serverName = client.ServerInfo is { } serverInfo ? $"{serverInfo.Name} {serverInfo.Version}" : "the server";
    Console.WriteLine($"Connected to {serverName} at {endpoint} (protocol {client.NegotiatedProtocolVersion})");
    Console.WriteLine();

    // 1. Capability check. Clients issue skills/list and skills/get only after observing the declaration.
    if (!client.ServerCapabilities.SupportsSkills())
    {
        Console.WriteLine("The server does not declare the io.modelcontextprotocol/skills extension.");
        return;
    }

    // 2. Enumerate. A listing may be empty or partial; a skill can always be fetched by URI.
    //    The per-page overload returns each page's caching hints with its entries. Only a missing cursor ends the
    //    listing. Use ListSkillsAsync() when page hints aren't needed. This sample bounds pages and refuses repeated cursors.
    Console.WriteLine("=== skills/list ===");
    const int MaxPages = 100;
    var requestParams = new ListSkillsRequestParams();
    var seenCursors = new HashSet<string>(StringComparer.Ordinal);
    for (int pageNumber = 1; ; pageNumber++)
    {
        if (pageNumber > MaxPages)
        {
            throw new InvalidOperationException($"The listing exceeded this sample's budget of {MaxPages} pages.");
        }

        ListSkillsResult page = await client.ListSkillsAsync(requestParams, cancellationToken);
        Console.WriteLine($"  page {pageNumber} caching hints: ttlMs={(page.TimeToLive is { } ttl ? ttl.TotalMilliseconds.ToString() : "(none)")} cacheScope={page.CacheScope?.ToString() ?? "(none)"}");

        foreach (Skill listed in page.Skills)
        {
            string manifest = listed.Resources.IsDynamic
                ? "dynamic (no digests)"
                : $"{listed.Resources.Entries.Count} file(s), {GetTotalSize(listed)} bytes";
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
            throw new InvalidDataException("The server repeated a skills/list pagination cursor.");
        }

        requestParams = new ListSkillsRequestParams { Cursor = page.NextCursor };
    }

    Console.WriteLine();

    // 3. Retrieve one skill by URI. The server's instructions reference this one, so a host confirms it directly.
    Console.WriteLine("=== skills/get ===");
    Console.WriteLine($"  server instructions: {client.ServerInstructions}");
    const string SkillUri = "skill://git-workflow/SKILL.md";
    Skill skill = await client.GetSkillAsync(SkillUri, cancellationToken: cancellationToken);
    if (skill.Resources.IsDynamic)
    {
        throw new NotSupportedException("This sample requires a static manifest to verify fetched content.");
    }

    Console.WriteLine($"  {skill.Uri} ({skill.Name})");
    foreach (SkillResource file in skill.Resources.Entries)
    {
        Console.WriteLine($"    {file.Uri}  {file.Size,6} bytes  {file.Digest}");
    }

    Console.WriteLine();

    // 4. ReadSkillResourceAsync checks manifest membership, URI, size and digest, not frontmatter.
    //    The host must independently verify frontmatter before loading, regardless of the server's authoring path.
    //    This sample parses it with YamlDotNet, a full YAML parser (see YamlFrontmatterReader.cs).
    Console.WriteLine("=== resources/read (verified) ===");
    ReadResourceResult skillFile = await client.ReadSkillResourceAsync(skill, SkillUri, cancellationToken: cancellationToken);
    if (skillFile.Contents.Single(content => new Uri(content.Uri) == new Uri(skill.Uri)) is not TextResourceContents skillText)
    {
        throw new NotSupportedException("This sample requires text SKILL.md contents for frontmatter verification.");
    }

    var fetchedFrontmatter = new YamlFrontmatterReader().Read(skillText.Text);
    if (!JsonNode.DeepEquals(fetchedFrontmatter, skill.Frontmatter))
    {
        throw new InvalidDataException("The fetched SKILL.md frontmatter differs from the held entry.");
    }

    Console.WriteLine($"  {SkillUri} manifest and frontmatter verified:");
    Console.WriteLine(Indent(skillText.Text));

    if (skill.Resources.Entries.FirstOrDefault(file => new Uri(file.Uri) != new Uri(skill.Uri)) is { } supportingEntry)
    {
        var supportingFile = await client.ReadSkillResourceAsync(skill, supportingEntry.Uri, cancellationToken: cancellationToken);
        string supportingSize = supportingFile.Contents.Single(content => new Uri(content.Uri) == new Uri(supportingEntry.Uri)) switch
        {
            TextResourceContents text => $"{text.Text.Length} chars",
            BlobResourceContents blob => $"{blob.DecodedData.Length} bytes",
            _ => throw new NotSupportedException("This sample supports text and blob resource contents."),
        };

        Console.WriteLine($"  {supportingEntry.Uri} verified ({supportingSize})");
    }

    Console.WriteLine();

    // 5. An unlisted file is a change to the skill. While acting on the held entry, the host must not read it.
    Console.WriteLine("=== unlisted file ===");
    const string UnlistedFileUri = "skill://git-workflow/references/UNLISTED.md";
    bool isListed = skill.Resources.Entries.Any(file => new Uri(file.Uri) == new Uri(UnlistedFileUri));
    try
    {
        await client.ReadSkillResourceAsync(skill, UnlistedFileUri, cancellationToken: cancellationToken);
    }
    catch (InvalidDataException e) when (!isListed)
    {
        Console.WriteLine($"  refused: {e.Message}");
    }
}

static long GetTotalSize(Skill skill)
{
    try
    {
        return skill.Resources.Entries!.Sum(file => file.Size);
    }
    catch (OverflowException exception)
    {
        throw new OverflowException(
            $"The manifest for '{skill.Uri}' has a total size greater than Int64.MaxValue.", exception);
    }
}

static string Indent(string text) =>
    string.Join(Environment.NewLine, text.TrimEnd().Split('\n').Select(line => "    | " + line.TrimEnd('\r')));
