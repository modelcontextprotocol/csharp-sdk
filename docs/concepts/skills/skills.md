---
title: Skills
description: Serve and consume Agent Skills over MCP with the Skills extension.
uid: skills
---

## Skills

The Skills extension lets an MCP server publish [Agent Skills](https://agentskills.io/) alongside its tools,
resources, and prompts. A skill is a directory of files, minimally a `SKILL.md` with YAML frontmatter, that gives
an agent structured workflow instructions. Over MCP, each file is an ordinary resource, and the extension adds two
methods for discovering skills and obtaining a verifiable manifest of their files:

- `skills/list` enumerates the skills a server serves, with pagination.
- `skills/get` returns the entry for a single skill by the URI of its `SKILL.md`.

Skills are provided by the `ModelContextProtocol.Extensions.Skills` package. The implementation follows the
[Skills extension specification](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx)
(SEP-2640, extension id `io.modelcontextprotocol/skills`).

### Overview

A skill entry (<xref:ModelContextProtocol.Extensions.Skills.Skill>) is a complete, point-in-time snapshot of a skill:

- `uri`: the resource URI of its `SKILL.md`, conventionally `skill://<skill-path>/SKILL.md`. The final segment
  of `<skill-path>` is the skill's name.
- `frontmatter`: the `SKILL.md` YAML frontmatter, rendered verbatim as a JSON object. `name` and `description`
  are always present; every other authored field passes through.
- `resources`: either a complete manifest of the skill's files, each with a SHA-256 digest and size
  (<xref:ModelContextProtocol.Extensions.Skills.SkillResource>), or the string `"dynamic"` for generated content
  that cannot be digested.

A host builds its registry from entries alone, then fetches files lazily with `resources/read` and verifies each
one against the manifest. That verification, and the rule that an unlisted file is a change to the skill, is what
lets a user's approval bind to specific content.

### Serving skills

The simplest way to serve skills is to keep each one in its own directory, as the Agent Skills specification
lays them out, and point `WithSkillsFromDirectory` at the parent:

```csharp
using ModelContextProtocol.Extensions.Skills;

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithSkillsFromDirectory(Path.Combine(AppContext.BaseDirectory, "Skills"), configure: options =>
    {
        options.TimeToLive = TimeSpan.FromMinutes(5);
        options.CacheScope = CacheScope.Public;
    });
```

Every immediate subdirectory containing a `SKILL.md` becomes a skill. The SDK reads the frontmatter from each
`SKILL.md`, computes every digest and size from the same bytes the resources serve, registers the file resources,
and publishes each skill at `skill://{name}/SKILL.md`, where `name` comes from the frontmatter. Pass a `uriPrefix`
such as `skill://acme/billing/` to place the skills under an organizational path.

For finer control, build skills individually with <xref:ModelContextProtocol.Extensions.Skills.McpServerSkill> and
register them with `WithSkills`:

```csharp
var gitWorkflow = McpServerSkill.CreateFromDirectory(Path.Combine(skillsRoot, "git-workflow"));

var refunds = McpServerSkill.Create(
    "skill://acme/billing/refunds/SKILL.md",
    [
        McpServerSkillFile.FromText("SKILL.md", skillMarkdown),
        McpServerSkillFile.FromText("examples/approved.md", approvedTemplate),
        new McpServerSkillFile { Path = "assets/logo.png", Content = logoBytes },
    ]);

builder.Services.AddMcpServer().WithHttpTransport().WithSkills([gitWorkflow, refunds]);
```

All of these validate the skill against the specification and throw <xref:System.ArgumentException> with a
specific message when, for example, the frontmatter `name` does not match the URI, `description` exceeds the
Agent Skills limit of 1024 characters, a resource URI escapes the skill's directory, `SKILL.md` is missing, or
the skill exceeds the per-skill limits of 512 files or 16 MiB. Frontmatter fields beyond `name`, `description`,
`compatibility`, and the shape of `metadata` pass through verbatim, as the specification requires; hosts compare
them against the file and ignore fields such as `allowed-tools` for MCP-origin skills. File contents are copied when the skill is created, so
later changes to a caller's buffer or to files on disk do not affect what is served. `CreateFromDirectory` does not
follow symbolic links, since a link can point outside the skill directory; it throws if it encounters one. File
names containing characters with URI syntax (such as `{`, `?`, or a space) are percent-encoded in the resource URIs.

#### Frontmatter

<xref:ModelContextProtocol.Extensions.Skills.SkillFrontmatter> reads the YAML frontmatter of a `SKILL.md` into a
<xref:System.Text.Json.Nodes.JsonObject> without a YAML library. It accepts the subset of YAML that Agent Skills
frontmatter uses: block mappings nested to any depth, block and flow sequences, plain, quoted, and block scalars,
and comments. Unquoted scalars are resolved per the YAML 1.2 core schema (`null`, booleans, integers, finite
floats, otherwise strings), matching the YAML libraries used by other SDKs and by hosts. That matters because a
host verifies a skill by parsing the fetched `SKILL.md` itself and comparing field by field against the published
entry; a value that one side types as a number and the other as a string is a verification failure. Quote values
such as version numbers that are meant to be strings.

Anchors, aliases, tags, complex keys, nested flow collections, and multi-line quoted scalars are valid YAML the
reader does not support; it rejects them with a <xref:System.FormatException> naming the construct. For such a
file, the `Create` and `CreateFromDirectory` overloads that take an explicit `JsonObject` supply the frontmatter
directly. That escape hatch covers only valid-but-unsupported YAML: a file with no frontmatter block, malformed
YAML, or invalid UTF-8 is rejected regardless, since no host could parse it either. When the reader can parse the
file, an explicit object must match it exactly, or the skill is rejected at construction rather than by every host.

#### Custom catalogs

When skills come from a database, a file share, or a large or generated catalog, implement
<xref:ModelContextProtocol.Extensions.Skills.IMcpSkillCatalog> and pass it to `WithSkills`:

```csharp
public sealed class DatabaseSkillCatalog(SkillRepository repository) : IMcpSkillCatalog
{
    public async ValueTask<McpSkillPage> ListAsync(string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken)
    {
        string tenant = GetTenant(context.User);
        var (skills, nextCursor) = await repository.GetPageAsync(tenant, cursor, pageSize: 50, cancellationToken);
        return new McpSkillPage { Skills = skills, NextCursor = nextCursor };
    }

    public ValueTask<Skill?> GetAsync(string uri, McpSkillRequestContext context, CancellationToken cancellationToken) =>
        repository.FindAsync(GetTenant(context.User), uri, cancellationToken);
}
```

Both methods receive an <xref:ModelContextProtocol.Extensions.Skills.McpSkillRequestContext> with the JSON-RPC request,
the caller's <xref:System.Security.Claims.ClaimsPrincipal> when the transport supplies one (the ASP.NET Core
transport does), and any items that incoming-message filters attached to the request.

A catalog supplies entries only; the skills' files must still be served as resources, since hosts read them with
`resources/read`. A catalog may list only part of what it serves, or nothing at all, as long as `GetAsync` answers
for every skill the server serves to the caller. Throw <xref:ModelContextProtocol.McpProtocolException> with
<xref:ModelContextProtocol.McpErrorCode.InvalidParams> for a cursor the catalog cannot interpret.
<xref:ModelContextProtocol.Extensions.Skills.InMemoryMcpSkillCatalog> is the built-in implementation over a fixed
set of entries and can be composed into a custom one.

#### Dynamic skills

A skill whose content is generated on demand cannot publish stable digests. Set its entry's `Resources` to
<xref:ModelContextProtocol.Extensions.Skills.SkillResources.Dynamic> and serve it through a catalog. Such a skill
offers no content integrity, and hosts may decline to load it.

#### Protocol versions and caching hints

`skills/list` results carry the base protocol's `ttlMs` and `cacheScope` list-caching attributes, configured through
<xref:ModelContextProtocol.Extensions.Skills.McpSkillsOptions>. Those attributes, and `resultType`, are defined
from protocol revision `2026-07-28`. The SDK emits them only on requests negotiated under that revision or later,
where they are required and default to `0` and `private` when unset, the same conservative defaults the built-in
list methods use. On earlier revisions, which reject them as unrecognized keys, they are omitted.

### Consuming skills

The client extension methods in <xref:ModelContextProtocol.Extensions.Skills.McpSkillsClientExtensions> cover the
host's side of the protocol:

```csharp
using ModelContextProtocol.Extensions.Skills;

if (!client.SupportsSkills())
{
    return; // Clients issue skills/list and skills/get only after observing the declaration.
}

// Enumerate, following pagination. The listing may be empty or partial.
IList<Skill> skills = await client.ListSkillsAsync();

// Retrieve one skill by URI, for example one referenced from server instructions.
Skill skill = await client.GetSkillAsync("skill://git-workflow/SKILL.md");

// Read a file and verify it against the held entry's digest and size.
ReadResourceResult contents = await client.ReadSkillResourceAsync(skill, skill.Uri);
```

`ListSkillsAsync` and `GetSkillAsync` validate every entry the server returns against the specification's structural
requirements and throw <xref:ModelContextProtocol.Extensions.Skills.SkillVerificationException> for an entry a host
must not load, such as one whose manifest omits its own `SKILL.md`, carries a malformed digest, or lists a file
outside the skill. `ReadSkillResourceAsync` throws the same exception when the
content's size or digest does not match the manifest, or when the URI is not listed in it at all. In both cases the
content must not be used. To recover, refresh the entry with `GetSkillAsync` and proceed from the new manifest;
because the manifest changed, any approval bound to the previous one is revoked and must be obtained again.

The lower-level overloads, `ListSkillsAsync(ListSkillsRequestParams)` and `GetSkillAsync(GetSkillRequestParams)`,
return one page or the raw result and expose the caching hints.
<xref:ModelContextProtocol.Extensions.Skills.SkillVerifier> exposes the verification and digest helpers for hosts
that read resources through other means.

### Security considerations

Skill content is instructional text delivered to a model and is therefore a prompt-injection surface. The
specification places most of the burden on hosts. In particular:

- `skills/list` and `skills/get` are registered as raw request handlers, so they do not pass through the request
  filters that guard the built-in resource methods, including the ASP.NET Core authorization filters. A listing
  therefore discloses frontmatter, file names, sizes, and digests to any caller the transport admits. If some
  callers must not see some skills, implement <xref:ModelContextProtocol.Extensions.Skills.IMcpSkillCatalog> and
  decide from the request context's user, and guard the corresponding resources the same way. The built-in
  in-memory catalog serves the same entries to every caller.
- Treat MCP-served skill content as untrusted model input, and tag it with its originating server when it enters
  the model's context.
- Digests are unsigned and come from the same server as the content. A match proves consistency between the entry
  and what was fetched, not that either is trustworthy.
- Bind any persisted per-skill approval to the entry's manifest. A later entry with a different manifest revokes it.
- Do not honor frontmatter fields that widen the model's permissions, such as `allowed-tools`, for MCP-origin skills
  without explicit user approval.
- Skill names are labels, not identifiers, and are not unique across servers. Identify a skill by the pair of server
  identity and URI.

The SDK implements digest and size verification and the unlisted-file rule. It does not verify frontmatter against
the fetched `SKILL.md` automatically. A host can do so with
<xref:ModelContextProtocol.Extensions.Skills.SkillFrontmatter.Parse(System.String)> on the fetched text and
<xref:System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode,System.Text.Json.Nodes.JsonNode)>
against the entry's frontmatter, treating a parse failure or a difference as a verification failure.

### Not implemented

The optional `resources/directory/read` method and its `directoryRead` capability setting are not implemented.
Servers built with this package do not declare `directoryRead`, and hosts must not call the method against them.

### Samples

- [SkillsServer](../../../samples/SkillsServer/README.md): a Streamable
  HTTP server serving two skills from directories on disk.
- [SkillsClient](../../../samples/SkillsClient/README.md): a client that
  connects to it and discovers, retrieves, and verifies them.
