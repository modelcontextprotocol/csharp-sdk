# Skills Client Sample

A console client that consumes [Agent Skills](https://agentskills.io/) over MCP through the Skills extension
([SEP-2640](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx)),
doing what a host's skill-loading path does:

1. Connects over Streamable HTTP and checks that the server declares `io.modelcontextprotocol/skills`
   (`client.SupportsSkills()`).
2. Enumerates the catalog with `skills/list` (`client.ListSkillsAsync()` follows pagination; the per-page
   overload exposes the `ttlMs` and `cacheScope` hints).
3. Retrieves one skill by URI with `skills/get` (`client.GetSkillAsync(uri)`), the way a host confirms a URI it
   found in server instructions.
4. Reads the skill's files with `resources/read` and verifies each against the manifest's digest and size
   (`client.ReadSkillResourceAsync(skill, uri)`).
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
  caching hints: ttlMs=300000 cacheScope=Public
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
  skill://git-workflow/SKILL.md verified:
    | ---
    | name: git-workflow
    ...

=== unlisted file ===
  refused: 'skill://git-workflow/references/UNLISTED.md' is not listed in the manifest of skill ...
```

## Notes

- Digest verification proves that the entry and the content are consistent. It is not a trust boundary: both come
  from the same server. Treat skill content as untrusted model input and tag it with its originating server.
- The SDK does not verify frontmatter automatically. A host can re-parse the fetched `SKILL.md` with
  `SkillFrontmatter.Parse` and compare it to the entry with `JsonNode.DeepEquals` before loading a skill.
