# Skills Server Sample

A Streamable HTTP MCP server that serves two [Agent Skills](https://agentskills.io/) through the MCP Skills
extension ([SEP-2640](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx)).

Each skill is a directory under [`Skills/`](Skills) with a `SKILL.md` and supporting files:

| Skill URI                                | Files                                                          |
| ---------------------------------------- | -------------------------------------------------------------- |
| `skill://git-workflow/SKILL.md`          | `SKILL.md`, `references/COMMIT_STYLE.md`, `templates/PULL_REQUEST.md` |
| `skill://acme/billing/refunds/SKILL.md`  | `SKILL.md`, `policy/REFUND_POLICY.md`, `examples/approved.md`, `examples/declined.md` |

`McpServerSkill.CreateFromDirectory` reads each directory, computes the SHA-256 digest and size of every file, and
produces both the skill's entry and the resources that serve its files. `WithSkills` registers the `skills/list`
and `skills/get` methods and the resources in one call.

The frontmatter is passed in code and must mirror the YAML at the top of each `SKILL.md` exactly. Hosts verify
this and refuse a skill whose entry disagrees with its file. The extension does not parse YAML.

## Run

```bash
dotnet run --project samples/SkillsServer/SkillsServer.csproj
```

The server listens on `http://localhost:3001` (see `Properties/launchSettings.json`). Then, in another terminal,
run the [SkillsClient](../SkillsClient) sample, which connects to it and walks through discovery, retrieval, and
verified reads:

```bash
dotnet run --project samples/SkillsClient/SkillsClient.csproj
```

Any MCP host that supports Streamable HTTP can also be pointed at `http://localhost:3001`.

## Notes

- Skill URIs are scoped to the server that serves them. The nested path `acme/billing/refunds` shows an
  organizational prefix; only the final segment must equal the skill's `name`.
- The server's instructions point the agent at `skill://git-workflow/SKILL.md` directly. A host can confirm a
  URI with `skills/get` and read it with `resources/read` without ever listing the catalog.
- The listing is identical for every caller, so it advertises `cacheScope: public` with a five-minute `ttlMs`.
  A catalog that varies by principal must not do that.
- See [docs/concepts/skills/skills.md](../../docs/concepts/skills/skills.md) for the full walkthrough,
  including how to implement a custom `IMcpSkillCatalog`.
