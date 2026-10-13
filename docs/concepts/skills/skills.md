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
  are always present; every other authored field passes through. The
  <xref:ModelContextProtocol.Extensions.Skills.Skill.Name>, <xref:ModelContextProtocol.Extensions.Skills.Skill.Description>,
  <xref:ModelContextProtocol.Extensions.Skills.Skill.License>, <xref:ModelContextProtocol.Extensions.Skills.Skill.Compatibility>,
  and <xref:ModelContextProtocol.Extensions.Skills.Skill.AllowedTools> properties read the string fields the Agent Skills
  specification defines.
- `resources`: either a complete manifest of the skill's files, each with a SHA-256 digest and size
  (<xref:ModelContextProtocol.Extensions.Skills.SkillResource>), or the string `"dynamic"` for generated content
  that cannot be digested.

A host builds its registry from entries alone, then fetches files lazily with `resources/read` and verifies each
one against the manifest. Before loading a skill, it also parses the fetched `SKILL.md` and compares its
frontmatter with the entry's; digest verification does not cover that. That verification, and the rule that an
unlisted file is a change to the skill, is what lets a user's approval bind to specific content.

Keeping the entry and the file in agreement is the server's job, and checking that they agree is the host's:

| Who | Responsibility |
|---|---|
| Server, using `McpServerSkill` | Frontmatter is read from `SKILL.md` and the manifest computed from the served bytes. With explicit frontmatter for YAML the reader does not support, the application must supply JSON identical to the file. |
| Server, using a custom catalog | The application supplies entries whose frontmatter and manifest match the files it serves. The SDK checks entry structure only. |
| Host | Verifies each file against the manifest, then parses the fetched `SKILL.md` and compares its frontmatter with the entry before loading. |

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

Every included immediate subdirectory containing a `SKILL.md` becomes a skill. The SDK reads the frontmatter from each
`SKILL.md`, computes every digest and size from the same bytes the resources serve, registers the file resources,
and publishes each skill at `skill://{name}/SKILL.md`, where `name` comes from the frontmatter. Pass a `uriPrefix`
such as `skill://acme/billing/` to place the skills under an organizational path.
Names used as URI path segments are percent-encoded. The SDK decodes the final name segment once
when comparing it with frontmatter; the supplied URI text remains the skill's identifier. Nested skills
sharing a file must use the identical URI text, including its encoding, so verified reads name the same resource.
Native absolute resource URIs such as `repo:/skills/refunds/SKILL.md` and `repo:skills/refunds/SKILL.md`
are also supported; an absolute resource URI does not need an authority introduced by `://`.

Dot-prefixed, hidden, and system files and directories are excluded by default, before reading their contents
or counting them toward the published manifest's limits. The `includeHiddenAndSystemFiles: true` overloads of
`WithSkillsFromDirectory` and `McpServerSkill.CreateFromDirectory` provide an explicit opt-in. For a narrower
selection, use `McpServerSkill.Create` with explicit files; that API keeps the files the caller selected.
Included symbolic links remain errors rather than being followed.

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

Register skills once per server. `WithSkills` and `WithSkillsFromDirectory` install the `skills/list` and `skills/get`
handlers, so a second call for the same service collection throws <xref:System.InvalidOperationException> before it
registers anything. To serve skills from several directories or sources, pass every `McpServerSkill` to one
`WithSkills` call, as above, or implement one `IMcpSkillCatalog` that combines them.

Each skill file must be the only resource at its URI. If another resource is registered at a URI that the server's
resource collection treats as the same (scheme and authority are compared without case), building the server's
options throws <xref:System.InvalidOperationException> naming the URI, whether that resource was registered before or
after the skills. Without this check, one of the two would be served in place of the other.

All of these validate structural entry and file constraints and throw <xref:System.ArgumentException> with a
specific message when, for example, the frontmatter `name` does not match the URI, `description` exceeds the
Agent Skills limit of 1024 Unicode scalar values, `compatibility` exceeds 500 Unicode scalar values,
`license` or `allowed-tools` is not a string, a resource URI escapes the skill's directory, `SKILL.md` is missing, or
the skill exceeds the per-skill limits of 512 files or 16 MiB. Every field the Agent Skills specification defines is
type-checked: `name`, `description`, `license`, `compatibility`, and `allowed-tools` must be strings, and `metadata` a
string-to-string map. Other frontmatter fields pass through verbatim, as the specification requires; hosts compare
them against the file. Hosts still ignore `allowed-tools` for MCP-origin skills unless the user has approved that grant. File contents are copied when the skill is created, so
later changes to a caller's buffer or to files on disk do not affect what is served. `CreateFromDirectory` does not
follow symbolic links, since a link can point outside the skill directory; it throws if it encounters one. File
names containing characters with URI syntax (such as `{`, `?`, or a space) are percent-encoded in the resource URIs.

The optional <xref:ModelContextProtocol.Extensions.Skills.Skill.Meta> property carries entry-level `_meta`.
It is separate from the frontmatter's string-to-string `metadata` map and may contain arbitrary JSON values.
Catalog snapshots preserve it without sharing mutable objects with callers.

#### Frontmatter

The `Create` and `CreateFromDirectory` overloads that do not take a `JsonObject` read the YAML frontmatter of
`SKILL.md` with the SDK's built-in reader, which needs no YAML library. It reads a bounded subset of YAML: block
mappings (nested up to 32 levels), block sequences, single-line flow collections, plain, quoted, and block scalars,
and comments. The root must be a mapping. Unquoted scalars are resolved per
the YAML 1.2 core schema (`null`, booleans, integers, finite floats, otherwise strings), the resolution the `yaml`
package and `yaml.v3` use; other parsers can differ at the edges. That matters because a host verifies a skill by
parsing the fetched `SKILL.md` itself with a full YAML parser and comparing field by field against the published
entry; a value that one side types as a number and the other as a string is a verification failure. Quote values
such as version numbers that are meant to be strings. Hexadecimal (`0x`) and octal (`0o`) integers keep their exact
value up to 64 bits, leading zeros included. Wider ones are reported as unsupported, because parsers disagree on
them: `yaml.v3` reads them as strings and the `yaml` package as rounded floating-point numbers.

Anchors, aliases, tags, complex (`? `) and empty mapping keys, nested and multi-line flow collections (including
`{items:[one]}`, and any flow collection with a comment inside, since a comment runs to the end of its line),
multi-line quoted scalars, nesting deeper than 32 levels, and hexadecimal or octal integers wider than 64 bits are
not supported; these overloads reject them with an <xref:System.ArgumentException> naming the construct rather than
guess. For such a file, the `Create` and `CreateFromDirectory` overloads that take an explicit `JsonObject` supply the
frontmatter directly. They still reject invalid UTF-8, a missing frontmatter block, and malformed YAML the reader
detects, and when the reader can parse the whole block, the supplied object must match it exactly. Every overload
also rejects, anywhere in the frontmatter, comments and quoted strings included, a raw character outside YAML's
printable set, such as a NUL, and a raw carriage return without a line feed, NEL, LS or PS. YAML 1.2.2 allows some of
the first kind in quoted strings, but libyaml, PyYAML and Go's `yaml.v3` reject them anywhere, and YAML parsers
disagree on whether the second kind ends a line. Write such a character as an escape in a double-quoted string, such
as `\0` or `\L`.

The reader can stop at the first unsupported construct, though, so for such a file it may not check the rest of the
block, and it cannot compare the block with the supplied object. The application must parse the entire frontmatter
with a full YAML parser and supply the identical JSON; the SDK accepting it is not proof that it matches.

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

The catalog passed to `WithSkills` is one instance shared by every request, so it must not capture scoped services,
such as a database context, directly. A catalog that needs scoped services can create its own scope for each call
with `IServiceScopeFactory`; such a scope is independent of the request's scope and does not share its instances.

A catalog supplies entries only; the skills' files must still be served as resources, since hosts read them with
`resources/read`. Each entry's frontmatter must match its `SKILL.md`; the SDK validates entry structure but does not
read the files to check this. A catalog may list only part of what it serves, or nothing at all, as long as
`GetAsync` answers for every skill the server serves to the caller. Throw
<xref:ModelContextProtocol.McpProtocolException> with <xref:ModelContextProtocol.McpErrorCode.InvalidParams> for a
cursor the catalog cannot interpret. <xref:ModelContextProtocol.Extensions.Skills.InMemoryMcpSkillCatalog> is the
built-in implementation over a fixed set of entries and can be composed into a custom one.

To show different callers different skills, filter the entries first and paginate only the filtered set. Every
page and continuation cursor then covers only skills the caller may read. Filtering a finished page while passing
on its cursor is not enough, because the cursor can still lead to hidden skills. Building an
`InMemoryMcpSkillCatalog` validates and copies its entries, so keep one filtered view per role or user instead of
building a new one on every request when that is expensive:

```csharp
public sealed class RoleSkillCatalog(IReadOnlyList<Skill> skills, SkillAccessPolicy policy) : IMcpSkillCatalog
{
    private readonly ConcurrentDictionary<string, InMemoryMcpSkillCatalog> _viewsByRole = new();

    public ValueTask<McpSkillPage> ListAsync(string? cursor, McpSkillRequestContext context, CancellationToken cancellationToken) =>
        GetView(context.User).ListAsync(cursor, context, cancellationToken);

    public ValueTask<Skill?> GetAsync(string uri, McpSkillRequestContext context, CancellationToken cancellationToken) =>
        GetView(context.User).GetAsync(uri, context, cancellationToken);

    // Authorize before paginating: the view holds only the skills this role may read.
    private InMemoryMcpSkillCatalog GetView(ClaimsPrincipal? user) =>
        _viewsByRole.GetOrAdd(policy.GetRole(user), role =>
            new InMemoryMcpSkillCatalog(skills.Where(skill => policy.CanRead(role, skill.Uri))));
}
```

Guard `resources/read` for the skills' files with the same policy, for example with a read-resource request filter.

#### Dynamic skills

A skill whose content is generated on demand cannot publish stable digests. Set its entry's `Resources` to
<xref:ModelContextProtocol.Extensions.Skills.SkillResources.Dynamic> and serve it through a catalog. Such a skill
offers no content integrity, and hosts may decline to load it. A host that does load one still verifies its
frontmatter.

#### Protocol versions and caching hints

`skills/list` and `skills/get` results carry the base protocol's `ttlMs` and `cacheScope` caching attributes,
configured through <xref:ModelContextProtocol.Extensions.Skills.McpSkillsOptions>. Those attributes, and
`resultType`, are defined from protocol revision `2026-07-28`. The SDK emits them only on requests negotiated under that revision or later,
where they are required and default to `0` and `private` when unset, the same conservative defaults the built-in
list methods use. On earlier revisions, which reject them as unrecognized keys, they are omitted. Both methods use
the same configured hints, so use `public` only when the listing and every entry are identical for every caller.

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

// Parse the fetched frontmatter into JSON with a full YAML parser of your choice; the SkillsClient sample uses
// YamlDotNet. A parse failure rejects this load. Do not fall back to skill.Frontmatter.
JsonObject fetchedFrontmatter = ParseFrontmatter(text.Text);
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

#### Listing limits and rejected entries

The aggregating `ListSkillsAsync()` overload stops at 100 pages or 1,000 entries by default. Repeated cursors
and exceeded limits throw `McpException`; no silently truncated list is returned. Empty cursors remain opaque
values, not an end marker. Use explicit positive limits when appropriate:

```csharp
var skills = await client.ListSkillsAsync(maxPages: 200, maxSkills: 5000);
```

Use the paged overload for application-controlled iteration. Strict listing still rejects a page containing
an invalid entry. To retain valid entries while reporting the others, opt into
`ListSkillsWithDiagnosticsAsync`:

```csharp
var listing = await client.ListSkillsWithDiagnosticsAsync(new ListSkillsRequestParams());
foreach (var rejected in listing.RejectedEntries)
{
    Console.Error.WriteLine($"Skill entry {rejected.Index} was rejected: {rejected.Error.Message}");
}
foreach (var listedSkill in listing.Page.Skills)
{
    Console.WriteLine(listedSkill.Uri);
}
```

`Page` preserves the original cursor and cache hints. `RejectedEntries` identifies each invalid entry by its
position in the original page and retains the parsing/validation error. An empty `Page.Skills` can mean every
entry was rejected, so callers must inspect the errors. A malformed page envelope or missing `skills` array
still fails the request. This API does not load rejected entries or replace file/frontmatter verification.

### Security considerations

Skill content is instructional text delivered to a model and is therefore a prompt-injection surface. The
specification places most of the burden on hosts. In particular:

- `skills/list` and `skills/get` are registered as raw request handlers, so they do not pass through the request
  filters that guard the built-in resource methods, including the ASP.NET Core authorization filters. A listing
  therefore discloses frontmatter, file names, sizes, and digests to any caller the transport admits. If some
  callers must not see some skills, implement <xref:ModelContextProtocol.Extensions.Skills.IMcpSkillCatalog> and
  decide from the request context's user, and guard the corresponding resources the same way. The built-in
  in-memory catalog serves the same entries to every caller. Use one shared application policy for both
  metadata and file access, and filter before paginating as shown in [Custom catalogs](#custom-catalogs), so
  continuation cursors cannot disclose hidden skill URIs. Filtering a completed page while forwarding its
  unfiltered cursor is not sufficient. Do not derive authorization from `resources/list` membership:
  listings may intentionally be partial while an authorized direct read remains valid.
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
the fetched `SKILL.md` automatically, and a host must do so before loading a skill, as the example above shows: parse
the fetched text with a full YAML parser of its choice, convert the frontmatter to JSON, and compare it with the
entry's frontmatter using
<xref:System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode,System.Text.Json.Nodes.JsonNode)>,
treating a parse failure or a difference as a verification failure. The
[SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsClient) sample shows one way,
with YamlDotNet. A host never uses the entry's frontmatter in place of parsing the file.

### Not implemented

The optional `resources/directory/read` method and its `directoryRead` capability setting are not implemented.
Servers built with this package do not declare `directoryRead`, and hosts must not call the method against them.

### Samples

- [SkillsServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsServer): a Streamable
  HTTP server serving two skills from directories on disk.
- [SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsClient): a client that
  connects to it and discovers, retrieves, and verifies them.
