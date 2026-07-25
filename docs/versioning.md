---
title: C# SDK Versioning
author: jeffhandley
description: ModelContextProtocol C# SDK approach to versioning, breaking changes, and support
uid: versioning
---
The ModelContextProtocol specification continues to evolve rapidly, and it's important for the C# SDK to remain current with specification additions and updates. To enable this, all NuGet packages that compose the SDK follow [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) with MAJOR.MINOR.PATCH version numbers, and optional prerelease versions.

Given a version number MAJOR.MINOR.PATCH, the package versions increment the:

* MAJOR version when incompatible API changes are included
* MINOR version when functionality is added in a backward-compatible manner
* PATCH version when backward-compatible bug fixes are included

*A prerelease version indicates that the version is unstable and might not satisfy the intended compatibility requirements.*

## Supported versions

The following support policy applies to stable C# ModelContextProtocol SDK packages:

1. New functionality and additive APIs will be introduced in MINOR releases within the current MAJOR version only.
    * New functionality will not be added to an earlier MAJOR version.
2. Bugs will be fixed in either:
    1. A PATCH release against the latest MAJOR.MINOR version within _the latest_ MAJOR version only.
    2. A MINOR release against the latest MAJOR version within _the latest_ MAJOR version only.
3. Bugs deemed by the maintainers to be critical or blocking will be fixed in both:
    1. A PATCH release against _the latest_ MAJOR version, within its latest MAJOR.MINOR version.
    2. A PATCH release against _one previous_ MAJOR version, within its latest MAJOR.MINOR version.

## Experimental APIs

MAJOR or MINOR version updates might introduce or alter APIs annotated as [`[Experimental]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute). This attribute indicates that an API is experimental and might change at any time&mdash;including within PATCH or MINOR version updates.

Experimental APIs require suppression of diagnostic codes specific to the MCP SDK APIs, using an `MCP` prefix.

## MCP specification compatibility

The 2.0.0 SDK implements the `2026-07-28` MCP specification revision while retaining compatibility with peers that negotiate [`2025-11-25`](https://modelcontextprotocol.io/specification/2025-11-25) and earlier. A v2 client automatically uses the legacy `initialize` handshake when it connects to a down-level server, and a v2 server continues to accept that handshake from a down-level client. Stable, non-deprecated 1.x APIs continue to work without modification on those connections.

### Tasks exception

For protocol-level compatibility, Tasks are the sole documented exception. The v2
[`Tasks`](xref:tasks) extension replaces the experimental Tasks implementation from v1.3.0 and
v1.4.x and is available only after negotiating `2026-07-28` or later. It has no API or wire
compatibility with the down-level implementation: a v2 Tasks client or server does not use
`tasks/*` on a `2025-11-25` connection.

## Breaking changes

The 2.0.0 SDK is a stable release. The SDK follows Semantic Versioning, and breaking changes against stable releases require increments to the MAJOR version.

If feasible, the SDK will support all versions of the MCP spec. However, if breaking changes to the spec make this infeasible, preference will be given to the most recent version of the MCP spec. This would be considered a breaking change necessitating a new MAJOR version.

All releases are posted to https://github.com/modelcontextprotocol/csharp-sdk/releases with release notes. Issues and pull requests labeled with `breaking-change` are highlighted in the corresponding release notes.

### Specification schema changes

If the MCP specification changes the schema for JSON payloads, the C# SDK might use the [`McpSession.NegotiatedProtocolVersion`](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.McpSession.html#ModelContextProtocol_McpSession_NegotiatedProtocolVersion) to dynamically change the payload schema, potentially using internal data transfer objects (DTOs) to achieve the needed deserialization behavior. These techniques will be applied where feasible to maintain backward-compatibility and forward-compatibility between MCP specification versions.

For illustrations of how this could be achieved, see the following prototypes:

* [Support multiple contents in sampling results](https://github.com/eiriktsarpalis/csharp-sdk/pull/2)
* [Support multiple contents in sampling results (using DTOs)](https://github.com/eiriktsarpalis/csharp-sdk/pull/3)

### Obsolete APIs

If APIs within the SDK become obsolete due to changes in the MCP spec or other evolution of the SDK's APIs, the [`[Obsolete]`](https://learn.microsoft.com/dotnet/api/system.obsoleteattribute) attribute will be applied to the affected APIs.

1. Within a MINOR version update, APIs might be marked as `[Obsolete]` to produce _build warnings_ while the API remains functional. The build warnings will provide guidance specific to the affected APIs.
2. Within a MAJOR version update, APIs might be marked as `[Obsolete]` to produce _build errors_ indicating the API is no longer functional and always throws exceptions. The build errors will provide guidance specific to the affected APIs.
3. Within a MAJOR version update, obsolete APIs might be removed. API removals are expected to be rare and avoided wherever possible, and `[Obsolete]` attributes will be applied ahead of the API removal.

Beginning with the 1.0.0 release, all obsoletions will use diagnostic codes specific to the MCP SDK APIs, using an `MCP` prefix.
