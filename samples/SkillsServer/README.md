# Skills Server Sample

A Streamable HTTP MCP server that serves [Agent Skills](https://agentskills.io/) through the MCP Skills
extension ([SEP-2640](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/beta/skills.mdx)).

Each skill is a directory under [`Skills/`](Skills) with a `SKILL.md` and supporting files:

| Skill URI                                | Files                                                          |
| ---------------------------------------- | -------------------------------------------------------------- |
| `skill://git-workflow/SKILL.md`          | `SKILL.md`, `references/COMMIT_STYLE.md`, `templates/PULL_REQUEST.md` |
| `skill://refunds/SKILL.md`               | `SKILL.md`, `policy/REFUND_POLICY.md`, `examples/approved.md`, `examples/declined.md` |

`WithSkillsFromSubdirectories` reads every skill directory, parses the YAML frontmatter of each `SKILL.md`, computes the
SHA-256 digest and size of every file, and registers the `skills/list` and `skills/get` methods together with the
resources that serve the files, in one call. Each skill's URI is `skill://{name}/SKILL.md`, where `name` comes from
its frontmatter; set `McpServerSkillPackageDiscoveryOptions.UriPrefix` to place skills under an organizational path
such as `skill://acme/billing/`.
Directory discovery excludes dot-prefixed, hidden and system entries by default. The explicit
`IncludeHiddenAndSystemFiles` option opts in; included links are still rejected.

Use `WithSkillFromDirectory` for a single skill directory or the standalone `McpServerSkillPackage` factories for
explicit authoring. `WithSkillPackages` registers each package's skill and resources through the ordinary,
independent first-wins paths. Configure runtime handlers and filters separately with `WithSkills(options => ...)`.

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

- Skill URIs are scoped to the server that serves them. A URI may carry an organizational prefix, as in
  `skill://acme/billing/refunds/SKILL.md`; only the final segment must equal the skill's `name`.
- The server's instructions point the agent at `skill://git-workflow/SKILL.md` directly. A host can confirm a
  URI with `skills/get` and read it with `resources/read` without ever listing the catalog.
- Registered operations use conservative caching hints (`private`, immediately stale). Application handlers and filters
  can provide per-result hints; public caching requires a result identical for every caller.
- See [docs/concepts/skills/skills.md](../../docs/concepts/skills/skills.md) for the full walkthrough,
  including the primitive collection and typed custom handlers that resolve request-scoped application services.
- The Skills APIs are experimental (`MCPEXP005`); this sample opts in through its project file.

## Database-style request handlers

[DatabaseStyleHandlers.cs](DatabaseStyleHandlers.cs) is a compact alternative configuration using the same
`context.Services.GetRequiredService<ExampleSkillRepository>()` pattern for Skills list/get and resource list/read.
It is compiled and tested, but not enabled by this directory-backed server. It is an alternative recipe for an
application with authentication, not an authenticated mode of the default server/client. Such an application
could register fixed sample rows and call `WithRepositoryHandlers()`:

```csharp
using SkillsServer;

builder.Services.AddSingleton(new ExampleSkillStore(
[
    ExampleSkillRow.Create("acme", "refunds"),
    ExampleSkillRow.Create("acme", "direct-only", listed: false),
]));
builder.Services.AddMcpServer().WithHttpTransport().WithRepositoryHandlers();
```

The store is **in-memory illustrative data, not a database implementation**. Supply an authenticated principal
with a `tenant` claim and the appropriate role using the application's authentication middleware. The example
does not create identities or implement an authentication scheme. The repository applies tenant and permission
predicates before pagination; direct skill lookup and file reads apply the same policy without requiring listing
membership. Real database code should execute those predicates and the page limit in the query and pass the
handler's cancellation token into async database calls. The fixed sample builds one SDK URI lookup index so get
and read resolve aliases to the same canonical row; it is not a pattern for loading an entire database into memory.
