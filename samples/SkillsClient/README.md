# Skills Client Sample

A console client that consumes [Agent Skills](https://agentskills.io/) over MCP through the Skills extension
([SEP-2640](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx)),
doing what a host's skill-loading path does:

1. Connects over Streamable HTTP and checks that the server declares `io.modelcontextprotocol/skills`
   (`client.SupportsSkills()`).
2. Pages through the catalog with `skills/list` using the per-page overload (`client.ListSkillsAsync(requestParams)`),
   so each page's `ttlMs` and `cacheScope` hints are shown with that page's entries. Only a missing cursor ends the
   listing; the sample stops after 100 pages and refuses a repeated cursor. When the hints are not needed,
   `client.ListSkillsAsync()` follows the pages itself, within 100 pages and 1,000 entries by default.
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

To run against another Streamable HTTP server, pass its endpoint:

```bash
dotnet run --project samples/SkillsClient/SkillsClient.csproj -- https://skills.example.com/mcp
```

Expected output (abridged):

```
=== skills/list ===
  page 1 caching hints: ttlMs=300000 cacheScope=Public
  skill://git-workflow/SKILL.md
    name: git-workflow
    manifest: 3 file(s), 1523 bytes
  skill://refunds/SKILL.md
    name: refunds
    ...

=== skills/get ===
  skill://git-workflow/SKILL.md (git-workflow)
    skill://git-workflow/SKILL.md     724 bytes  sha256:…
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
  typing, and compares it to the entry. It rejects the skill on a difference, on invalid YAML, on a raw character the
  server also rejects (one outside YAML's printable set, or a CR without a line feed, NEL, LS or PS, which YamlDotNet
  would accept, drop or read as a line break), on a tag it does not convert, on frontmatter JSON cannot represent, or
  on frontmatter past its limits on nesting depth and alias expansion, including an alias cycle. A host can use any
  full YAML parser, but never the entry's own frontmatter.
- This is not a complete host: approval binding, origin isolation, and permission decisions are left out.
- The ordinary list helpers remain strict. `ListSkillsWithDiagnosticsAsync` is an explicit alternative
  for obtaining valid entries together with `RejectedEntries`; it never treats an invalid entry as loadable.
