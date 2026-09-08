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

The simplest way to serve skills is to describe each one with
<xref:ModelContextProtocol.Extensions.Skills.McpServerSkill> and register them with `WithSkills`. The SDK computes
every digest and size from the same bytes the resources serve, so the manifest and the content cannot disagree,
and registers the file resources for you.

```csharp
using ModelContextProtocol.Extensions.Skills;
using System.Text.Json.Nodes;

var gitWorkflow = McpServerSkill.CreateFromDirectory(
    uri: "skill://git-workflow/SKILL.md",
    frontmatter: new JsonObject
    {
        ["name"] = "git-workflow",
        ["description"] = "Follow this team's Git conventions for branching and commits.",
    },
    directoryPath: Path.Combine(AppContext.BaseDirectory, "Skills", "git-workflow"));

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithSkills([gitWorkflow], options =>
    {
        options.TimeToLive = TimeSpan.FromMinutes(5);
        options.CacheScope = CacheScope.Public;
    });
```

`McpServerSkill.Create` takes the files explicitly when they are not on disk:

```csharp
var skill = McpServerSkill.Create(
    "skill://refunds/SKILL.md",
    new JsonObject { ["name"] = "refunds", ["description"] = "Process refunds." },
    [
        McpServerSkillFile.FromText("SKILL.md", skillMarkdown),
        McpServerSkillFile.FromText("examples/approved.md", approvedTemplate),
        new McpServerSkillFile { Path = "assets/logo.png", Content = logoBytes },
    ]);
```

Both methods validate the skill against the specification and throw <xref:System.ArgumentException> with a
specific message when, for example, the frontmatter `name` does not match the URI, `SKILL.md` is missing, or the
skill exceeds the per-skill limits of 512 files or 16 MiB. File contents are copied when the skill is created, so
later changes to a caller's buffer or to files on disk do not affect what is served. `CreateFromDirectory` does not
follow symbolic links, since a link can point outside the skill directory; it throws if it encounters one. File
names containing characters with URI syntax (such as `{`, `?`, or a space) are percent-encoded in the resource URIs.

#### Frontmatter

The frontmatter is supplied as a <xref:System.Text.Json.Nodes.JsonObject> and must reproduce the YAML frontmatter
at the top of `SKILL.md` exactly, field by field. Hosts re-parse the fetched `SKILL.md` and compare it against the
entry, treating any discrepancy as a verification failure. The SDK does not include a YAML parser and does not
derive the frontmatter from the file, so keep the two in sync.

#### Custom catalogs

When skills come from a database, a file share, or a large or generated catalog, implement
<xref:ModelContextProtocol.Extensions.Skills.IMcpSkillCatalog> and pass it to `WithSkills`:

```csharp
public sealed class DatabaseSkillCatalog(SkillRepository repository) : IMcpSkillCatalog
{
    public async ValueTask<McpSkillPage> ListAsync(string? cursor, CancellationToken cancellationToken)
    {
        var (skills, nextCursor) = await repository.GetPageAsync(cursor, pageSize: 50, cancellationToken);
        return new McpSkillPage { Skills = skills, NextCursor = nextCursor };
    }

    public ValueTask<Skill?> GetAsync(string uri, CancellationToken cancellationToken) =>
        repository.FindAsync(uri, cancellationToken);
}
```

A catalog supplies entries only; the skills' files must still be served as resources, since hosts read them with
`resources/read`. A catalog may list only part of what it serves, or nothing at all, as long as `GetAsync` answers
for every skill the server serves. Throw <xref:ModelContextProtocol.McpProtocolException> with
<xref:ModelContextProtocol.McpErrorCode.InvalidParams> for a cursor the catalog did not issue.
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

`ReadSkillResourceAsync` throws <xref:ModelContextProtocol.Extensions.Skills.SkillVerificationException> when the
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
the fetched `SKILL.md`, since it does not parse YAML; a host must do that itself before loading a skill.

### Not implemented

The optional `resources/directory/read` method and its `directoryRead` capability setting are not implemented.
Servers built with this package do not declare `directoryRead`, and hosts must not call the method against them.

### Samples

- [SkillsServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsServer): a Streamable
  HTTP server serving two skills from directories on disk.
- [SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsClient): a client that
  connects to it and discovers, retrieves, and verifies them.
