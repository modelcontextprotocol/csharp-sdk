---
title: OAuth Proxy
description: Bridge MCP clients to OAuth providers that do not support Dynamic Client Registration.
uid: oauth-proxy
---

# OAuth proxy

MCP clients normally discover an authorization server and register themselves dynamically. Many
enterprise identity providers require applications to be registered ahead of time instead. The
ASP.NET Core OAuth proxy bridges these models by exposing an MCP-compatible authorization server
while using one fixed upstream application registration.

The proxy:

- accepts Dynamic Client Registration from MCP clients;
- keeps each client's callback URI and PKCE challenge separate from the upstream flow;
- uses an independent PKCE verifier and a fixed callback URI with the upstream provider;
- stores upstream tokens encrypted on the server;
- returns only application-minted, short-lived access tokens to MCP clients; and
- rotates opaque proxy refresh handles without exposing upstream refresh tokens.

## Configure the proxy

Register an `IMcpOAuthProxyStore`, configure the proxy, and map its endpoints. The issuer path must
match the path passed to `MapMcpOAuthProxy`.

```csharp
var proxyIssuer = new Uri("https://mcp.example.com/oauth");
var tokenIssuer = new ProxyTokenIssuer(/* signing credentials */);

// The application store must atomically consume one-time records. For example, a Redis-backed
// implementation can use GETDEL. See Storage requirements below.
builder.Services.AddSingleton<IMcpOAuthProxyStore, RedisMcpOAuthProxyStore>();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("oauth-proxy", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

builder.Services.AddMcpOAuthProxy(options =>
{
    options.Issuer = proxyIssuer;

    // The proxy discovers authorization and token endpoints from this issuer.
    options.UpstreamIssuer = new Uri(
        "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0");
    options.UpstreamClientId = builder.Configuration["OAuth:ClientId"]!;
    options.UpstreamClientSecret = builder.Configuration["OAuth:ClientSecret"]!;
    options.UpstreamRedirectUri = new Uri("https://mcp.example.com/oauth/callback");

    options.AllowedScopes.Add("openid");
    options.AllowedScopes.Add("profile");
    options.AllowedScopes.Add("offline_access");
    options.AllowedScopes.Add("api://mcp-server/mcp:tools");
    options.AllowedResources.Add("https://mcp.example.com");

    options.ClientRegistrationValidator = (context, cancellationToken) =>
        context.HttpContext.RequestServices
            .GetRequiredService<ClientRegistrationPolicy>()
            .ValidateAsync(context, cancellationToken);
    options.AuthorizationValidator = (context, cancellationToken) =>
        context.HttpContext.RequestServices
            .GetRequiredService<DownstreamAuthorizationPolicy>()
            .ValidateAsync(context, cancellationToken);

    options.TokenFactory = tokenIssuer.IssueAsync;
    options.JwksUri = new Uri("https://mcp.example.com/.well-known/jwks.json");
});

var app = builder.Build();
app.MapMcpOAuthProxy("/oauth").RequireRateLimiting("oauth-proxy");
```

`ProxyTokenIssuer` is application code. Its `IssueAsync` method receives an
<xref:ModelContextProtocol.AspNetCore.Authentication.McpOAuthProxyTokenContext> and returns an
<xref:ModelContextProtocol.AspNetCore.Authentication.McpOAuthProxyTokenResult>. It should validate
the upstream identity, mint a new JWT for the proxy issuer and MCP resource, and return the JWT's
subject, audience, and unique token identifier for the structured audit event.

The proxy rejects token-factory results that:

- equal an upstream access, refresh, or ID token;
- are not Bearer tokens;
- have empty subject, audience, or token identifiers; or
- exceed `MaximumAccessTokenLifetime`.

Configure the MCP server's JWT Bearer handler to validate the proxy-issued token, not the upstream
provider token. The MCP protected-resource metadata should advertise `proxyIssuer` as its
authorization server.

Both policy callbacks are required. `ClientRegistrationValidator` should verify an initial access
token, trusted redirect URI, or equivalent registration policy. `AuthorizationValidator` should
enforce user and client approval before the proxy uses its upstream application registration. The
OAuth endpoints allow anonymous HTTP access so they continue to work with ASP.NET Core fallback
authorization policies; the callbacks are therefore the application security boundary.

Every accepted `resource` value must appear exactly in `AllowedResources`. This prevents a dynamic
client from choosing an arbitrary audience for the proxy-issued token. The proxy also constrains
minted scopes to the set returned by the upstream authorization server.

## Explicit upstream endpoints

For an upstream provider without OpenID Connect discovery, configure both endpoints explicitly:

```csharp
options.UpstreamAuthorizationEndpoint = new Uri("https://idp.example.com/authorize");
options.UpstreamTokenEndpoint = new Uri("https://idp.example.com/token");
```

The proxy supports `client_secret_basic`, `client_secret_post`, and public upstream clients through
`UpstreamClientAuthenticationMethod`. Provider-specific parameters can be supplied through
`AdditionalAuthorizationParameters` and `AdditionalTokenParameters`; standard OAuth parameters
cannot be overridden.

## Storage requirements

The proxy protects every stored record with ASP.NET Core Data Protection before passing it to
<xref:ModelContextProtocol.AspNetCore.Authentication.IMcpOAuthProxyStore>. Persist the Data
Protection key ring and restrict access to both the key ring and the cache. Losing or deleting keys
that still protect active records invalidates registrations and refresh handles. Store keys contain
only one-way digests, not bearer credentials or client callback URIs.

`IDistributedCache` does not define an atomic get-and-delete operation. The provided
<xref:ModelContextProtocol.AspNetCore.Authentication.DistributedCacheMcpOAuthProxyStore> prevents
replay within one process, but it is not registered automatically because it cannot provide atomic
consumption across multiple application instances. A single-instance application can register it
explicitly. Multi-instance deployments must provide an `IMcpOAuthProxyStore` whose `TakeAsync`
operation is atomic in the backing store.

Do not use an in-memory distributed cache in production. Registrations and refresh mappings must
survive application restarts, and every instance must share the same protected records.

Opaque refresh handles rotate after every successful use. Reuse of a consumed handle revokes the
current descendant handle for that token family. Retryable upstream failures restore the handle for
only the remainder of its original absolute lifetime.

## Security requirements

- Serve the issuer, callback, and MCP resource over HTTPS. HTTP is accepted only for loopback
  development addresses.
- Register the exact fixed `UpstreamRedirectUri` with the upstream provider.
- Keep upstream client credentials and Data Protection keys outside source control.
- Keep `CookieSecurePolicy.Always`, the default, outside loopback development.
- Apply request-rate limits to the public registration, authorization, and token endpoints.
- Keep both proxy policy callbacks default-deny and audit their approval decisions.
- Treat token-mint audit records as security data. They contain subject, audience, scopes, client
  ID, token ID, and mint time, but never token values.
- Keep access-token lifetimes short and configure refresh-token and registration lifetimes for the
  application's revocation requirements.

The proxy requires S256 PKCE on both the client-to-proxy and proxy-to-upstream legs. Authorization
callbacks are additionally bound to the browser that initiated the transaction through a
short-lived, HTTP-only cookie.
