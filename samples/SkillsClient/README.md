# Skills Client Sample

A console client that consumes [Agent Skills](https://agentskills.io/) over MCP through the Skills extension
([SEP-2640](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/beta/skills.mdx)),
doing what a host's skill-loading path does:

1. Connects over Streamable HTTP and checks that the server declares `io.modelcontextprotocol/skills`
   (`client.ServerCapabilities.Extensions`).
2. Pages through the catalog with `skills/list` using `client.ListSkillsAsync(requestParams)`,
   so each page's `ttlMs` and `cacheScope` hints are shown with that page's entries. Only a missing cursor ends the
   listing; the sample stops after 100 pages and refuses a repeated cursor. Both safeguards are this sample's
   application policy, not behavior added by the SDK's single-page method.
3. Retrieves one skill by URI with `skills/get` (`client.GetSkillAsync(uri)`), the way a host confirms a URI it
   found in server instructions.
4. Reads the skill's files with `resources/read` and verifies manifest membership, URI, digest and size
   (`client.ReadSkillResourceAsync(skill, uri)`). Before displaying `SKILL.md`, parses its frontmatter with
   YamlDotNet and compares every field with the held entry.
5. Shows that a file the manifest does not list is refused before any request is sent.

## Run

Start the [SkillsServer](../SkillsServer) sample in one terminal:

```bash
dotnet run --project samples/SkillsServer/SkillsServer.csproj
```

Then run the client in another:

```bash
dotnet run --project samples/SkillsClient/SkillsClient.csproj
```

To run against another instance of this sample server, pass its endpoint:

```bash
dotnet run --project samples/SkillsClient/SkillsClient.csproj -- https://skills.example.com/mcp
```

Press Ctrl+C to cancel connection or an in-flight operation and exit cleanly. The same cancellation token is
forwarded through listing, get, and verified reads.

Expected output (abridged; byte counts and digests vary with checkout line endings):

```
=== skills/list ===
  page 1 caching hints: ttlMs=0 cacheScope=Private
  skill://git-workflow/SKILL.md
    name: git-workflow
    manifest: 3 file(s), <total bytes> bytes
  skill://refunds/SKILL.md
    name: refunds
    ...

=== skills/get ===
  skill://git-workflow/SKILL.md (git-workflow)
    skill://git-workflow/SKILL.md     <bytes> bytes  sha256:<digest>
    ...

=== resources/read (verified) ===
  skill://git-workflow/SKILL.md manifest and frontmatter verified:
    | ---
    | name: git-workflow
    ...

=== unlisted file ===
  refused: 'skill://git-workflow/references/UNLISTED.md' is not listed in the manifest of skill ...
```

## Notes

- Digest verification proves that the entry and the content are consistent. It is not a trust boundary: both come
  from the same server. Treat skill content as untrusted model input and tag it with its originating server.
- Digest verification is not frontmatter verification. The sample also parses the fetched `SKILL.md` frontmatter
  with YamlDotNet, a full YAML parser (`YamlFrontmatterReader.cs`), converts it to JSON with YAML 1.2 core-schema
  typing, and compares it to the entry. A difference or parse failure rejects the load. See the
  [Skills guide](../../docs/concepts/skills/skills.md) for parsing and verification details.
- This is not a complete host: approval binding, origin isolation, and permission decisions are left out.
- The sample requires text `SKILL.md` contents and a single matching content item. These are sample limitations,
  not SDK restrictions.
- Listing is strict: a malformed or invalid entry fails the page rather than being silently omitted.
  Applications own pagination and any count, byte, or time budget; the SDK imposes no per-skill file-count or
  total-byte ceiling.
- Client entry-validation and integrity failures use `InvalidDataException`, without a Skills-specific exception
  subclass. RPC failures retain the usual MCP exception types.
- The Skills APIs are experimental (`MCPEXP005`); this sample opts in through its project file.
