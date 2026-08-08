---
title: Authorization
author: anneheartrecord
description: How to protect a production MCP server with OAuth 2.0 using an existing identity provider.
uid: authorization
---

# Authorization

This article covers how to protect an MCP server with OAuth 2.0 in production: which half of the protocol you are expected to implement, how to wire the SDK up to an identity provider you already run, and how to enforce access at the endpoint and at the individual tool.

## Your MCP server is a resource server, not an authorization server

OAuth splits the work between two parties, and the MCP authorization specification keeps that split:

| Role | Who implements it | Responsibilities |
| - | - | - |
| Authorization server | Your identity provider — Microsoft Entra ID, Auth0, Okta, Keycloak, and similar | Authenticate the user, obtain consent, issue and refresh access tokens, publish signing keys, register clients |
| Resource server | Your MCP server | Validate the bearer token on each request, advertise where a token can be obtained, serve MCP |

The SDK's ASP.NET Core integration implements the resource server half only. `AddMcp()` contributes three things:

- the [RFC 9728](https://datatracker.ietf.org/doc/html/rfc9728) protected resource metadata endpoint, which tells clients which authorization servers to use,
- the `WWW-Authenticate` challenge on `401` responses, which points clients at that metadata document, and
- a `ForwardAuthenticate` default of `Bearer`, so that authenticating with the MCP scheme delegates to the `JwtBearer` scheme instead of returning no result — which is why pointing `DefaultScheme` at the MCP scheme also works.

Token validation itself is ordinary ASP.NET Core `JwtBearer`. There is no API in the SDK for issuing tokens, because a production MCP server should not be issuing its own.

> [!IMPORTANT]
> The `ModelContextProtocol.TestOAuthServer` project under `tests/` is a test fixture. It exists so that the end-to-end authorization tests and the `ProtectedMcpServer` sample can run on localhost without external accounts, which is why it implements key handling, token minting, and client registration from scratch. It is not a template to copy. In production you replace it with your identity provider — the resource server code in the sample stays essentially as it is.

## Choosing an authorization server

Any spec-compliant OAuth 2.0 authorization server works. Before committing to one, check that it can do the following, because these are the capabilities MCP leans on:

- **Register your MCP server as an API with its own audience**, so issued tokens carry an `aud` claim matching your resource URI ([RFC 8707](https://datatracker.ietf.org/doc/html/rfc8707) resource indicators). Without this you cannot validate the audience, which means a token minted for an unrelated API would be accepted by your server.
- **Publish discovery metadata** at `/.well-known/oauth-authorization-server` or `/.well-known/openid-configuration`. Clients follow your protected resource metadata to the authorization server and then read its metadata to find the authorization and token endpoints.
- **Support PKCE**, which OAuth 2.1 requires for public clients.
- **Support [Dynamic Client Registration](https://datatracker.ietf.org/doc/html/rfc7591), or client ID metadata documents.** This is the requirement most often overlooked. MCP clients such as desktop agents and IDE extensions are not yours to pre-register, so unless the authorization server can register them on demand, only clients you have provisioned by hand can connect.

## Configuring the server

A production configuration differs from the sample mainly in that `Authority` points at a real identity provider:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// The canonical, publicly reachable URI of this MCP server. Tokens must be scoped to it.
var resource = "https://mcp.example.com";
var authority = "https://login.example.com/tenant-id/v2.0";

builder.Services.AddAuthentication(options =>
{
    // Challenge with the MCP scheme so 401s carry the resource_metadata pointer,
    // but authenticate with JwtBearer so tokens are validated normally.
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Signing keys are discovered from the authority's metadata document and refreshed automatically.
    options.Authority = authority;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authority,
        ValidateAudience = true,
        ValidAudience = resource,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        NameClaimType = "name",
        RoleClaimType = "roles",
    };
})
.AddMcp(options =>
{
    options.ResourceMetadata = new()
    {
        Resource = resource,
        ResourceName = "Example MCP server",
        AuthorizationServers = { authority },
        ScopesSupported = ["mcp:tools"],
    };
});

builder.Services.AddAuthorization();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<MyTools>()
    .AddAuthorizationFilters();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp().RequireAuthorization();

app.Run();
```

`ValidAudience` is the part worth dwelling on. It is what stops a token issued for a different API in the same tenant from being replayed against your MCP server, so it needs to match the `Resource` you advertise, and your identity provider needs to be configured to mint tokens with that audience.

## Publishing protected resource metadata

`AddMcp()` serves the metadata document at `/.well-known/oauth-protected-resource`, and at `/.well-known/oauth-protected-resource/<path>` for a server that hosts several MCP endpoints under different paths. The path suffix mirrors the endpoint being protected, so each endpoint can advertise a distinct resource identifier.

When <xref:ModelContextProtocol.Authentication.ProtectedResourceMetadata.Resource> is left unset, the handler derives it from the incoming request — scheme, host, port, path base, and the path suffix. That inference only applies to the default endpoint. If you set <xref:ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions.ResourceMetadataUri> to move the document elsewhere, you must also set `Resource` explicitly, or the request fails with an `InvalidOperationException`.

Setting `Resource` explicitly is the safer default in production regardless, because the derived value depends on how the request reached your process.

### Behind a reverse proxy or TLS terminator

Because both the derived resource identifier and the `resource_metadata` URL in the `WWW-Authenticate` header are built from the incoming request, a proxy that terminates TLS will cause your server to advertise `http://` and its internal hostname unless forwarded headers are honored. Clients then attempt to fetch metadata from a URL that is wrong or unreachable.

Configure [forwarded headers](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer) so that the scheme, host, and path base are reconstructed before authentication runs. `UseForwardedHeaders` processes nothing unless you name the headers you want, so the options matter as much as the middleware:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedPrefix;

    // Without this, any caller that reaches the app directly can forge these headers.
    options.AllowedHosts = ["mcp.example.com"];
});

// ...

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();
```

`AllowedHosts` is not optional here. An empty list means every value of `X-Forwarded-Host` is accepted, and a request that bypasses the ingress could then dictate both the resource identifier you advertise and the metadata URL in your challenge header. Restricting known proxies with `KnownProxies` achieves the same thing from the other direction.

`XForwardedPrefix` is the one to remember when your ingress strips a path prefix, since it is what restores `PathBase`. Pinning `Resource` to a literal string sidesteps the problem for the metadata document, but the URL in the challenge header is always derived from the request.

### Per-request metadata for multi-tenant servers

A single deployment that serves several tenants needs to point each one at a different authorization server. Use <xref:ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationEvents.OnResourceMetadataRequest> to build the document per request:

```csharp
.AddMcp(options =>
{
    options.Events.OnResourceMetadataRequest = context =>
    {
        var host = context.HttpContext.Request.Host;
        var tenant = host.Host.Split('.')[0];

        context.ResourceMetadata = new()
        {
            Resource = $"https://{host}",
            AuthorizationServers = { $"https://login.example.com/{tenant}/v2.0" },
            ScopesSupported = ["mcp:tools"],
        };

        return Task.CompletedTask;
    };
});
```

Token validation has to become per-tenant as well. Resolving the issuer and signing keys dynamically — for example through `TokenValidationParameters.IssuerSigningKeyResolver` and a tenant-aware `IssuerValidator` — is the usual approach, and is standard `JwtBearer` configuration rather than anything MCP-specific.

## Enforcing access

There are two layers, and production servers generally want both.

**At the endpoint.** `app.MapMcp().RequireAuthorization()` requires an authenticated principal for every MCP request. This is coarse — all of MCP or none of it — and it runs in the ASP.NET Core authorization middleware, before any MCP method is dispatched.

**At the individual primitive.** `[Authorize]` and `[AllowAnonymous]` work on tool, prompt, and resource methods, and on the types that contain them. The example below leaves `MapMcp()` ungated so that the anonymous tool stays reachable:

```csharp
[McpServerToolType]
public class MixedAccessTools
{
    [McpServerTool, Description("Looks up public reference data.")]
    [AllowAnonymous]
    public static string Lookup(string term) => Reference.Find(term);

    [McpServerTool, Description("Returns the caller's billing summary.")]
    [Authorize(Policy = "McpTools")]
    public static string BillingSummary(ClaimsPrincipal user) => Billing.SummaryFor(user);

    [McpServerTool, Description("Rotates a tenant API key.")]
    [Authorize(Roles = "Admin")]
    public static string RotateKey(string tenantId) => Keys.Rotate(tenantId);
}
```

> [!IMPORTANT]
> `[AllowAnonymous]` is the one attribute that does not combine with endpoint-level gating. `RequireAuthorization()` rejects unauthenticated callers before MCP dispatches anything, so a primitive marked `[AllowAnonymous]` behind it is still unreachable without a token. If some primitives are genuinely public, leave `MapMcp()` ungated and let the attributes decide. Stacking `RequireAuthorization()` with per-primitive `[Authorize]` is fine, and is the usual production shape.

These attributes are enforced by the filters that `AddAuthorizationFilters()` installs, so that call is required. It is not optional cleanup: if a primitive carries authorization metadata and the filters were never registered, the server throws an `InvalidOperationException` naming `AddAuthorizationFilters()` rather than quietly granting access. The exception surfaces in the server log; the client sees a generic error. The failure mode is closed, but you still have to make the call.

Listing operations are filtered too. `tools/list`, `prompts/list`, `resources/list`, and `resources/templates/list` return only the primitives the caller is authorized to use, so an under-privileged caller never sees admin tooling in the listing. Filtering applies to primitives registered in the server's collections — those added by `WithTools`, `WithPrompts`, `WithResources`, and similar. Items produced by a custom list handler are outside the collections and are returned unfiltered, so a handler that synthesizes its own list has to apply its own checks.

### Scope-based policies

Scopes are the natural unit of MCP authorization, since they are what the client requests consent for and what you advertise in `ScopesSupported`. Most providers deliver them in a single space-delimited `scope` claim, and some use `scp` instead, so a policy has to split the value rather than match it whole:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpTools", policy =>
        policy.RequireAuthenticatedUser().RequireAssertion(context =>
            context.User.FindAll("scope")
                .Concat(context.User.FindAll("scp"))
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains("mcp:tools")));
});
```

`policy.RequireClaim("scope", "mcp:tools")` looks equivalent but is not — it compares the whole claim value, so it fails as soon as the token carries more than one scope.

## Reading identity inside handlers

Once a request is authenticated, the `ClaimsPrincipal` flows into tool, prompt, and resource handlers. Declare a `ClaimsPrincipal` parameter and the SDK injects it without adding it to the tool's input schema. See [Identity and roles](xref:identity) for the details.

## Sessions, scaling, and claim freshness

[Stateless mode](xref:stateless) is the default and the better fit for most authenticated deployments. Each request is validated on its own, nothing is pinned to a particular instance, and you can scale horizontally without session affinity.

Claim freshness is not a reason to avoid sessions. The `ClaimsPrincipal` the authorization filters evaluate is taken from the current HTTP request each time a message is read, not captured once when the session was created, so a token presented with fewer roles or scopes is evaluated with those reduced claims on the very next request. What a stateful session does pin is *who* the caller is: the server records the `sub`, `NameIdentifier`, or `UPN` claim from the request that initiated the session and rejects any later request bearing a different one with `403 Forbidden`.

Two kinds of staleness do exist, and neither involves `[Authorize]`. The first is `IHttpContextAccessor` on the legacy SSE transport, where the ambient `HttpContext` refers to the long-lived SSE connection rather than the POST being handled — see [HTTP context](xref:httpcontext). Taking identity from the injected `ClaimsPrincipal` rather than from `HttpContext.User` avoids it. The second is `ConfigureSessionOptions`, which runs once per session when sessions are enabled: anything you decide there from the initiating request's claims — a filtered tool list, for instance — is fixed for the life of the session, unlike the attribute-based checks.

## What the existing samples demonstrate

| Project | Role | Use in production? |
| - | - | - |
| [ProtectedMcpServer](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpServer) | Resource server: `JwtBearer` validation, `AddMcp()` metadata, CORS, stateless transport | Yes — this is the shape of a production server. Repoint `Authority` and the metadata at your identity provider. |
| [ProtectedMcpClient](https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpClient) | Client performing the authorization code flow with PKCE | Yes, as a reference for building an MCP client |
| `tests/ModelContextProtocol.TestOAuthServer` | Authorization server implemented from scratch for tests | No — replace it with your identity provider |

## Checklist

- The MCP server validates tokens; it does not issue them.
- `ValidateAudience` is on, and `ValidAudience` matches the advertised `Resource`.
- `ValidateIssuer` and `ValidateLifetime` are on, and `Authority` is HTTPS.
- `Resource` is set explicitly rather than inferred, and forwarded headers are configured if a proxy is in front.
- `AddAuthorizationFilters()` is called, and sensitive primitives carry `[Authorize]`.
- Endpoint-level `RequireAuthorization()` and per-primitive `[AllowAnonymous]` are not both being relied on.
- Scope policies split the `scope` claim instead of matching it whole.
- Stateless mode is left at its default unless something genuinely requires sessions.
