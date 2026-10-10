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
one against the manifest. Before loading `SKILL.md`, it must also parse the fetched YAML and compare every
frontmatter field with the held entry. Digest and size verification alone does not establish that equality.

### Responsibility boundaries

The protocol carries structured metadata; it does not require a particular parser or provider.

| Layer | Responsibility |
|---|---|
| Application / catalog | Supply frontmatter identical to the complete frontmatter in the file, and serve matching file bytes and manifests. Structural entry validation does not read or validate the YAML on a catalog's behalf. |
| Optional automatic authoring helper | `McpServerSkill.Create` / `CreateFromDirectory` without explicit metadata use the bounded `SkillFrontmatter` reader. Unsupported or malformed input is rejected, not replaced with guessed metadata. |
| Explicit-metadata authoring | Keep the helper's enforced checks. If the bounded reader cannot fully read unsupported YAML, the application must parse and validate the entire frontmatter with a suitable mechanism and supply the correct JSON. |
| Host | Independently parse fetched `SKILL.md` and compare its frontmatter with the held entry before loading, regardless of server authoring path. Reject unsupported input or use a suitable full parser; server-provided JSON is not verification. |

### Serving skills

The simplest way to serve skills is to keep each one in its own directory, as the Agent Skills specification
lays them out, and point `WithSkillsFromDirectory` at the parent:

```csharp
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

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

All of these validate structural entry and file constraints and throw <xref:System.ArgumentException> with a
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
<xref:System.Text.Json.Nodes.JsonObject> without a YAML library. This is an optional bounded reader, not a
requirement for serving structured metadata through a catalog. It supports block mappings (nested up to 32 levels), block and flow sequences, plain, quoted, and block
scalars, and comments. Unquoted scalars are resolved per the YAML 1.2 core schema (`null`, booleans, integers, finite
floats, otherwise strings). YAML parsers can differ in their supported forms and resolution; identical semantics
across other SDKs or libraries are not assumed. That matters because a
host verifies a skill by parsing the fetched `SKILL.md` itself and comparing field by field against the published
entry; a value that one side types as a number and the other as a string is a verification failure. Quote values
such as version numbers that are meant to be strings.

Automatic authoring requires the bounded reader to succeed. Unsupported constructs and detected malformed input
raise an error; no fallback object is invented. Unsupported forms include anchors, aliases, tags, complex keys,
nested and multi-line flow collections, and multi-line quoted scalars.

The explicit `JsonObject` overloads retain UTF-8 and frontmatter-boundary checks, entry/URI validation, and manifest
digest/size computation from the served bytes. They also reject format errors detected by the reader and reject
metadata that differs from a frontmatter block the reader can parse completely.

**The explicit overloads do not guarantee whole-document YAML validation.** An unsupported construct can stop
parsing before later content is examined. The application must parse and validate the **entire frontmatter**
with a suitable mechanism and supply identical JSON before relying on that path. An unsupported-format error
is not proof that the rest of the document is valid, and acceptance of supplied metadata is not proof of equality.
This boundary does not relax the specification's requirement that published frontmatter match the file.

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
`resources/read`. The application/catalog author must supply frontmatter identical to the file. The transport
validates entry structure, not full file contents, and does not require use of the optional authoring reader.
A catalog may list only part of what it serves, or nothing at all, as long as `GetAsync` answers
for every skill the server serves to the caller. Throw <xref:ModelContextProtocol.McpProtocolException> with
<xref:ModelContextProtocol.McpErrorCode.InvalidParams> for a cursor the catalog cannot interpret.
<xref:ModelContextProtocol.Extensions.Skills.InMemoryMcpSkillCatalog> is the built-in implementation over a fixed
set of entries and can be composed into a custom one.

#### Dynamic skills

A skill whose content is generated on demand cannot publish stable digests. Set its entry's `Resources` to
<xref:ModelContextProtocol.Extensions.Skills.SkillResources.Dynamic> and serve it through a catalog. Such a skill
offers no content integrity, and hosts may decline to load it.
If a host accepts a dynamic skill, parsing and comparing its fetched frontmatter is still required.

#### Protocol versions and caching hints

`skills/list` and `skills/get` results carry the base protocol's `ttlMs` and `cacheScope` caching attributes, configured through
<xref:ModelContextProtocol.Extensions.Skills.McpSkillsOptions>. Those attributes, and `resultType`, are defined
from protocol revision `2026-07-28`. The SDK emits them only on requests negotiated under that revision or later,
where they are required and default to `0` and `private` when unset, the same conservative defaults the built-in
list methods use. On earlier revisions, which reject them as unrecognized keys, they are omitted. Both methods use
the same configured hints. Only use `public` when both the listing and individual entries are identical for every caller.

### Consuming skills

The client extension methods in <xref:ModelContextProtocol.Extensions.Skills.McpSkillsClientExtensions> cover the
host's side of the protocol:

```csharp
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

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

// This example loads text SKILL.md resources and rejects other content forms.
if (contents.Contents.Single(content => content.Uri == skill.Uri) is not TextResourceContents text)
{
    throw new SkillVerificationException("A text SKILL.md is required for this example.");
}

// A parse failure, including unsupported YAML, rejects this load. Do not fall back to skill.Frontmatter.
var fetchedFrontmatter = SkillFrontmatter.Parse(text.Text);
if (!JsonNode.DeepEquals(fetchedFrontmatter, skill.Frontmatter))
{
    throw new SkillVerificationException("The fetched frontmatter differs from the held entry.");
}
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
the fetched `SKILL.md` automatically. **The host must perform that independent check before loading**, regardless
of whether the server used a catalog, automatic authoring, or explicit metadata. For the supported subset it can use
<xref:ModelContextProtocol.Extensions.Skills.SkillFrontmatter.Parse(System.String)> on the fetched text and
<xref:System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode,System.Text.Json.Nodes.JsonNode)>
against the entry's frontmatter. Unsupported content must be rejected or parsed with a suitable full parser;
malformed input or a difference is a verification failure. Never use the server's JSON as a substitute for parsing
the fetched file. These examples do not implement the host's complete approval, origin-isolation or loading policy.

### Not implemented

The optional `resources/directory/read` method and its `directoryRead` capability setting are not implemented.
Servers built with this package do not declare `directoryRead`, and hosts must not call the method against them.

### Samples

- [SkillsServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/ecb11e621b0ae46685314729a74c0cb8fe283d44/samples/SkillsServer): a Streamable
  HTTP server serving two skills from directories on disk.
- [SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/ecb11e621b0ae46685314729a74c0cb8fe283d44/samples/SkillsClient): a client that
  connects to it and discovers, retrieves, and verifies them.
