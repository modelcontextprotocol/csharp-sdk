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
[Skills extension specification](https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/beta/skills.mdx)
(SEP-2640, extension id `io.modelcontextprotocol/skills`).

> [!IMPORTANT]
> Skills support is experimental. All public APIs use `[Experimental("MCPEXP005")]`; suppress that diagnostic
> specifically where you intentionally consume the package. SEP-2640 is Final, but the extension specification
> is Beta. See [MCPEXP005](xref:list-of-diagnostics#mcpexp005).

### Overview

A skill entry (<xref:ModelContextProtocol.Extensions.Skills.Skill>) describes one skill:

- `uri`: the resource URI of its `SKILL.md`, conventionally `skill://<skill-path>/SKILL.md`. The final segment
  of `<skill-path>` is the skill's name.
- `frontmatter`: the `SKILL.md` YAML frontmatter, rendered verbatim as a JSON object. `name` and `description`
  are required; other authored fields pass through. The DTO provides accessors for the standard string fields.
- `resources`: either a complete manifest of the skill's files, each with a SHA-256 digest and size
  (<xref:ModelContextProtocol.Extensions.Skills.SkillResource>), or the string `"dynamic"` for generated content
  that cannot be digested.

Enumerated files are available through `skill.Resources.Entries`. A `"dynamic"` manifest has null `Entries`,
not an empty collection; the array-or-string JSON representation remains under the name `resources`.

A host fetches files lazily through `resources/read`. Before loading a skill, it verifies the files against the
manifest and independently parses `SKILL.md` to compare its frontmatter with the entry.

| Who | Responsibility |
|---|---|
| Server, using `McpServerSkillPackage` | Frontmatter is read from `SKILL.md` and the manifest computed from the authored resource bytes. With explicit frontmatter for YAML the reader does not support, the application must supply JSON identical to the file. |
| Server, using a custom get implementation, handlers, or an existing entry | The application supplies entries whose frontmatter and manifest match the files it serves. The SDK checks entry structure only. |
| Host | Verifies each file against the manifest, then parses the fetched `SKILL.md` and compares its frontmatter with the entry before loading. |

### Serving skills

Use file-authored registrations for fixed content, application-owned sources for request-time data, and typed
filters to wrap either path.

#### File-authored registrations

The simplest way to serve skills is to keep each one in its own directory, as the Agent Skills specification
lays them out, and point `WithSkillsFromSubdirectories` at the parent:

```csharp
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithSkillsFromSubdirectories(Path.Combine(AppContext.BaseDirectory, "Skills"));
```

Every included immediate subdirectory containing a `SKILL.md` becomes a skill. The SDK reads the frontmatter from each
`SKILL.md`, computes every digest and size from the same bytes the resources serve, registers the file resources,
and publishes each skill at `skill://{name}/SKILL.md`, where `name` comes from the frontmatter. Set `McpServerSkillPackageDiscoveryOptions.UriPrefix`
such as `skill://acme/billing/` to place the skills under an organizational path.
Names used as URI path segments are percent-encoded.

Dot-prefixed, hidden, and system files and directories are excluded by default, before reading their contents.
The discovery and single-package options provide `IncludeHiddenAndSystemFiles` for an explicit opt-in. For a narrower
selection, use `McpServerSkillPackage.Create` with explicit files; that API keeps the files the caller selected.
Included symbolic links remain errors rather than being followed.

For finer control, author packages with <xref:ModelContextProtocol.Extensions.Skills.McpServerSkillPackage> and
register them with `WithSkillPackages`:

```csharp
using System.Text.Json.Nodes;

var gitWorkflow = McpServerSkillPackage.CreateFromDirectory(
    Path.Combine(skillsRoot, "git-workflow"),
    new McpServerSkillPackageCreateOptions
    {
        Metadata = ["skill-reader"],
        Meta = new JsonObject { ["example.com/category"] = "workflow" },
    });

var refunds = McpServerSkillPackage.Create(
    [
        McpServerSkillFile.FromText("SKILL.md", skillMarkdown),
        McpServerSkillFile.FromText("examples/approved.md", approvedTemplate),
        new McpServerSkillFile { Path = "assets/logo.png", Content = logoBytes },
    ],
    new McpServerSkillPackageCreateOptions { Uri = "skill://acme/billing/refunds/SKILL.md" });

builder.Services.AddMcpServer().WithHttpTransport().WithSkillPackages([gitWorkflow, refunds]);
```

<xref:ModelContextProtocol.Extensions.Skills.McpServerSkillPackageCreateOptions> supplies an optional `Uri`, explicit
`Frontmatter`, directory file-selection policy, server-local `Metadata`, and entry-level protocol `Meta`.
Each package contains a registered primitive in `Skill` and a read-only list of ordinary file `Resources`.
The primitive provides its entry for listing/get, and its resources serve the authored bytes. Local metadata is
not serialized, copied to file resources, or interpreted as authorization. Registering a package does not authorize
file reads; protect metadata and file resources separately with the same application policy.
The single-directory builder, `WithSkillFromDirectory`, authors one directory. `WithSkillsFromSubdirectories`
discovers immediate child directories in ordinal order. Its separate discovery options provide `UriPrefix`,
`IncludeHiddenAndSystemFiles`, and common `Metadata`/`Meta`, not per-skill URI or frontmatter overrides.

The factories do not require DI. `CreateFromSubdirectories` also returns standalone packages.
Register components separately with `WithSkills([package.Skill])` and `WithResources(package.Resources)`,
or register both with `WithSkillPackages([package])`.

### Consuming skills

The client extension methods in <xref:ModelContextProtocol.Extensions.Skills.McpSkillsClientExtensions> cover the
host's side of the protocol:

```csharp
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

if (!client.ServerCapabilities.SupportsSkills())
{
    return; // Clients issue skills/list and skills/get only after observing the declaration.
}

// Retrieve all listing pages. A server may list fewer skills than it serves.
IList<Skill> listedSkills = await client.ListSkillsAsync();
foreach (Skill listed in listedSkills)
{
    Console.WriteLine(listed.Uri);
}

// Retrieve one skill by URI, for example one referenced from server instructions.
Skill skill = await client.GetSkillAsync("skill://git-workflow/SKILL.md");

// Read a file and verify it against the held entry's digest and size.
ReadResourceResult contents = await client.ReadSkillResourceAsync(skill, skill.Uri);

// This example loads text SKILL.md resources and rejects other content forms.
if (contents.Contents.Single(content => new Uri(content.Uri) == new Uri(skill.Uri)) is not TextResourceContents text)
{
    throw new NotSupportedException("A text SKILL.md is required for this example.");
}

// Parse the fetched frontmatter into JSON with a full YAML parser of your choice; the SkillsClient sample uses
// YamlDotNet. A parse failure rejects this load. Do not fall back to skill.Frontmatter.
JsonObject fetchedFrontmatter = ParseFrontmatter(text.Text);
if (!JsonNode.DeepEquals(fetchedFrontmatter, skill.Frontmatter))
{
    throw new InvalidDataException("The fetched frontmatter differs from the held entry.");
}
```

List/get methods validate each entry; verified reads check membership, requested URI, size and digest.
Entry or integrity failures use <xref:System.IO.InvalidDataException>. Do not use failed content.
An unlisted file is refused before a request is sent. Refresh the entry to read newly added files; a changed
manifest revokes approval bound to the previous manifest.

`ListSkillsAsync(ListSkillsRequestParams)` and `GetSkillAsync(GetSkillRequestParams)` expose the result's caching hints.
The convenience list, get, and verified file-read methods accept standard `RequestOptions` for metadata and progress
tokens, followed by cancellation. Single-page listing accepts metadata through `ListSkillsRequestParams.Meta`.
Verified reads forward their options to `resources/read`.
<xref:ModelContextProtocol.Extensions.Skills.SkillVerifier> exposes the verification and digest helpers for hosts
that read resources through other means.

#### Pagination and malformed results

`ListSkillsAsync()` follows all listing pages and returns their entries as an `IList<Skill>`, matching the
tools/prompts/resources convenience methods. `ListSkillsAsync(ListSkillsRequestParams)` sends one request and
preserves its cursor and cache hints. Only a null cursor ends pagination; an empty string remains opaque.
The aggregating overload has no page/entry ceiling or repeated-cursor detection. Supply cancellation, or use
the single-page overload to enforce application-owned budgets.

A missing or null required skills array is not treated as an empty listing.
High-level operations reject null result members and null list entries. A structurally invalid entry fails its page
and aggregation rather than being silently omitted. Low-level DTO serialization still permits explicit null result
members; required-member presence and the manifest's array-or-`"dynamic"` wire form are checked separately.
RPC failures retain the usual MCP exception behavior.

### Application-owned sources

#### Registered get implementations

A registration can publish a `ProtocolSkill` listing entry, or leave it null for direct retrieval only.
Listing and startup never invoke `GetAsync`. Listing entries and get results must be structurally valid and
match the registration's immutable `Id`. Get receives the actual request context and cancellation token.

For example, an application repository can provide a get-only registration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

builder.Services.AddScoped<RunbookRepository>();
builder.Services.AddMcpServer().WithHttpTransport().WithSkills([new RunbookSkill()]);

sealed class RunbookSkill() : McpServerSkill("skill://runbook/SKILL.md")
{
    public override IReadOnlyList<object> Metadata => ["runbook-reader"];

    public override async ValueTask<GetSkillResult> GetAsync(
        RequestContext<GetSkillRequestParams> request, CancellationToken cancellationToken = default) => new()
    {
        Skill = await request.Services!.GetRequiredService<RunbookRepository>()
            .GetEntryAsync(request.User, cancellationToken),
        TimeToLive = TimeSpan.Zero,
        CacheScope = CacheScope.Private,
    };
}
```

`RunbookRepository` is application code returning the matching entry. Override `ProtocolSkill` to also publish a
listing entry, or use `ListSkillsHandler` for request-dependent discovery. `McpServerSkill.Create(entry, metadata)`
wraps an existing entry. None of these paths creates file resources.

Async retrieval does not require a `"dynamic"` manifest: it can return a complete digest/size manifest.
`"dynamic"` explicitly gives up content verification; hosts may decline it and must still verify frontmatter if accepted.

#### List/get handlers

For application-owned sources, configure typed list/get handlers and resolve dependencies from `context.Services`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;

builder.Services.AddScoped<ExampleSkillRepository>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithSkills(options =>
    {
        options.ListSkillsHandler = (context, cancellationToken) =>
            context.Services!.GetRequiredService<ExampleSkillRepository>()
                .ListSkillsAsync(context.User, context.Params.Cursor, cancellationToken);
        options.GetSkillHandler = async (context, cancellationToken) => new GetSkillResult
        {
            Skill = await context.Services!.GetRequiredService<ExampleSkillRepository>()
                .FindSkillAsync(context.User, context.Params.Uri, cancellationToken) ??
                throw new McpProtocolException("Skill is unavailable.", McpErrorCode.InvalidParams),
        };
    });
```

Serve and authorize files separately through the ordinary resource handlers. The complete, tested
[database-style recipe](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/samples/SkillsServer/DatabaseStyleHandlers.cs)
includes list/get/read handlers and an illustrative **in-memory** store, not a database or authentication implementation.
It applies tenant/permission checks before pagination and permits direct access to authorized unlisted skills.
Real database code should execute these predicates, cursor ordering and page limits in its query, propagating
cancellation. Do not load an entire database into the sample's fixed in-memory index.

Registered entries are appended to the initial custom list page (null cursor); continuation pages remain
application-owned. Return a fresh mutable page and do not list registrations again in the callback.
Registered gets take precedence over the custom fallback. Filtering callback entries cannot hide registrations
appended afterwards; use a typed list filter for those.

For caller-dependent sources, authorize before taking a page and derive cursors only from allowed returned rows.
Custom get handlers cannot authorize registered skills; use [typed Skills filters](#typed-request-filters).
Registered resource matching likewise wins over a custom read fallback. Do not mutate shared registrations per caller.

#### Typed request filters

`ListSkillsFilters` and `GetSkillFilters` use <xref:ModelContextProtocol.Server.McpRequestFilter`2>.
They run in registration order with the handler's context, services, caller, items and cancellation token.
Resolve scoped dependencies inside the returned handler. Get filters receive the matched primitive, including
its local `Metadata`, in `context.MatchedPrimitive`; list filters see the complete augmented page after `next`.
A filter may short-circuit, but its result still passes final structural validation and get URI binding.

For example, this fixed collection is visible only to authenticated callers with the `skill-reader` role. The Skills
and resource-list filters return empty listings for other callers; the get and file-read filters deny access
independently:

```csharp
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using System.Security.Claims;

var report = McpServerSkillPackage.Create(
    [McpServerSkillFile.FromText("SKILL.md", "---\nname: report\ndescription: Report workflow\n---\n")]);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithSkillPackages([report])
    .WithSkills(options =>
    {
        options.ListSkillsFilters.Add(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            if (!CanReadSkills(context.User))
            {
                result.Skills = [];
            }

            result.TimeToLive = TimeSpan.Zero;
            result.CacheScope = CacheScope.Private;
            return result;
        });
        options.GetSkillFilters.Add(next => (context, cancellationToken) =>
        {
            if (!CanReadSkills(context.User))
            {
                throw new McpProtocolException("Skill is unavailable.", McpErrorCode.InvalidParams);
            }

            return next(context, cancellationToken);
        });
    })
    .WithRequestFilters(filters =>
    {
        filters.AddListResourcesFilter(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            if (!CanReadSkills(context.User))
            {
                result.Resources = [];
            }

            return result;
        });
        filters.AddReadResourceFilter(next => (context, cancellationToken) =>
        {
            if (!CanReadSkills(context.User))
            {
                throw new McpProtocolException("Access forbidden.", McpErrorCode.InvalidRequest);
            }

            return next(context, cancellationToken);
        });
    });

static bool CanReadSkills(ClaimsPrincipal? user) =>
    user?.Identity?.IsAuthenticated == true && user.IsInRole("skill-reader");
```

This example filters unpaginated static Skills and resource listings. For paginated sources, authorize before forming
pages and cursors; filtering afterwards cannot repair a cursor that reveals hidden entries. Listing omission never
denies get or file read. Skills and resource filters are independent, with no automatic policy inheritance.
Automatic participation in ASP.NET Core
`AddAuthorizationFilters` is deferred and tracked by [#1990](https://github.com/modelcontextprotocol/csharp-sdk/issues/1990).

### Advanced reference

#### Configuration and file ownership

Registration is additive: repeated calls install wire handlers once and add primitives with `TryAdd`, preserving
insertion order and the first accepted identity. Configure handlers and filters separately with `WithSkills(options => ...)`.
DI contributions are aggregated during `Configure` before that callback; `PostConfigure` runs afterwards.
Later handler assignments win, and null restores collection-backed behavior.

`CreateFromSubdirectories` and `WithSkillPackages` validate their complete input batch before returning or adding
services. Shared file URIs must agree on digest and size, including ignored duplicate skill identities.
Rejected batches leave services unchanged. Empty package registration is valid; discovery without skills is an error.

Skills and file resources are registered independently with ordinary first-wins behavior. Removing or replacing
either does not update the other. Package authoring initially binds the manifest to its file bytes; applications
own agreement after entry mutations or resource overrides. Generic collections and lists validate individual
entries, not cross-entry snapshots. Verified reads remain authoritative for manifest/content agreement.

Factories capture options settings but may retain supplied frontmatter, entry `_meta` and local metadata references.
Applications own those mutable objects; use separate objects for response-only changes. Package file bytes are
copied and the resource collection is read-only. Textual MIME types use text only when bytes round-trip through
UTF-8; other content is served as a blob.

File/directory factories eagerly retain selected files without a count, byte or time budget. Use them on
application-controlled inputs, or select/read bounded files yourself. The SDK imposes no per-skill file/byte ceiling;
the specification requires hosts to support 512 files and 16 MiB, permits larger skills, and says servers **SHOULD NOT**
exceed that envelope for interoperability.

#### YAML frontmatter limits

The built-in reader supports a bounded YAML 1.2 core-schema subset: block mappings/sequences, single-line flow
collections, plain/quoted/block scalars and comments, with a mapping root and nesting up to 32 levels.
Anchors, aliases, tags, complex or empty keys, multi-line quoted/flow forms, and hexadecimal/octal integers wider
than 64 bits require explicit `McpServerSkillPackageCreateOptions.Frontmatter`. Parse the entire file with a full
YAML parser first and supply identical JSON. Quote values such as version numbers that must remain strings.

Explicit frontmatter does not bypass UTF-8, boundary or detected syntax errors. When the built-in reader can
parse the whole block, it compares supplied frontmatter field by field. If it stops at an unsupported construct,
the application owns validation of the remaining block and its agreement with the supplied object.
Raw non-printable characters, lone CR, NEL, LS and PS are rejected for parser interoperability; use quoted escapes.

#### Request services and transport

Use `context.Services`, not the server's potentially longer-lived provider. DI supplies it automatically;
low-level `McpServer.Create` must receive a provider if handlers need services. Resolve scoped dependencies per
request, rather than capturing them in long-lived callbacks.

The scope follows the same lifecycle as built-in MCP request handlers:

| Transport or configuration | Request services |
|---|---|
| Stateless Streamable HTTP, including modern clients on a hybrid endpoint | The existing `HttpContext.RequestServices`. Scoped state set by HTTP middleware is preserved; Skills does not create another scope. ASP.NET Core owns and disposes it. |
| Stream/stdio, stateful Streamable HTTP, and legacy SSE | When a scope factory is available and `McpServerOptions.ScopeRequests = true` (the default), Core creates an independent scope per MCP request and disposes it asynchronously after the handler finishes, including on failure or cancellation. This is not the HTTP middleware's scope. |
| `McpServerOptions.ScopeRequests = false` | The provider supplied to the server is used as-is and remains caller-owned. Supply an appropriately scoped provider when using scoped dependencies. |

Do not dispose request providers or retain their scoped services. With request scoping disabled, stream/stateful
requests can share the supplied root provider; legacy SSE shares its long-lived GET scope. Disposable transients
remain retained until that provider is disposed. Do not share a `DbContext` concurrently. Shared callbacks and
singletons must support concurrency; Skills creates no provider or nested scope.

#### Custom-handler protocol boundaries

Low-level `McpServerOptions.RequestHandlers` callbacks own their JSON. Typed Skills handlers instead return
`ListSkillsResult` or `GetSkillResult`; the SDK validates entries after all filters, binds gets to the original URI,
and shapes protocol/cache fields. Invalid request parameters use `InvalidParams`; server-result failures retain
Core's generic internal-error wire response and local diagnostics.

#### Protocol versions and caching hints

For `2026-07-28+`, both methods emit `resultType: "complete"` and default unset cache hints to `ttlMs: 0` and
`cacheScope: "private"`. Earlier protocols omit all three fields. Only serialized responses are shaped;
shared application results retain their discriminator and hints.

Set hints on handler results or in typed filters. Registered `GetAsync` can supply get hints; a custom get fallback
does not intercept registrations. List hints cover the **complete augmented page**, even when an empty custom
result supplies hints for static registrations. Use `public` only if every returned entry is identical for every caller.

### Security considerations

Skill content is instructional text delivered to a model and is therefore a prompt-injection surface. The
specification places most of the burden on hosts. In particular:

- Apply one policy to Skills and their files using [caller-filtered sources](#listget-handlers) or
  [typed request filters](#typed-request-filters). Neither listing omission nor `resources/list` membership
  authorizes resource access.
- Treat MCP-served skill content as untrusted model input, and tag it with its originating server when it enters
  the model's context.
- Digests are unsigned and come from the same server as the content. A match proves consistency between the entry
  and what was fetched, not that either is trustworthy.
- Bind any persisted per-skill approval to the entry's manifest. A later entry with a different manifest revokes it.
- Do not honor frontmatter fields that widen the model's permissions, such as `allowed-tools`, for MCP-origin skills
  without explicit user approval.
- Skill names are labels, not identifiers, and are not unique across servers. Identify a skill by the pair of server
  identity and URI.

Digest verification does not replace independent frontmatter parsing. The
[SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsClient) sample demonstrates
both checks with YamlDotNet before using content.

### Not implemented

The optional `resources/directory/read` method and its `directoryRead` capability setting are not implemented.
Servers built with this package do not declare `directoryRead`, and hosts must not call the method against them.

### Samples

- [SkillsServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsServer): a Streamable
  HTTP server serving two skills from directories on disk.
- [SkillsClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/SkillsClient): a client that
  connects to it and discovers, retrieves, and verifies them.
