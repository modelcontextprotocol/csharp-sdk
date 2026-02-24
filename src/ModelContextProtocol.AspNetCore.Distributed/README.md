# ModelContextProtocol.AspNetCore.Distributed

Session-aware routing for Model Context Protocol (MCP) servers that need to run across multiple instances. This package builds on ASP.NET Core HybridCache and YARP so every MCP request reaches the server that owns the session state.

## Why Use It

- Keep in-memory session data (prompt history, tool context) with its owning instance
- Scale stateful MCP servers horizontally without changing tool handlers
- Forward requests automatically when the owning instance lives elsewhere
- Plug in any `IDistributedCache` (Redis, SQL Server, NCache, etc.) for distributed storage

## Install

```bash
dotnet add package ModelContextProtocol.AspNetCore.Distributed --prerelease
```

Add the distributed cache provider that matches your environment (for example `Microsoft.Extensions.Caching.StackExchangeRedis`).

## Quick Start (Single Instance / Local Dev)

```csharp
using ModelContextProtocol.AspNetCore.Distributed;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMcpServer()
    .WithToolsFromAssembly()
    .WithHttpTransport();

builder.Services.AddMcpHttpSessionAffinity();     // Tracks ownership + routing

var app = builder.Build();

app.MapMcp()
   .WithSessionAffinity(); // Add this to enable session affinity routing

app.Run();
```

No distributed cache is required until you add additional instances.

## Production Checklist

1. Register an L2 cache (Redis + Azure AD auth is the most battle-tested option).
2. Set `LocalServerAddress` to the routable address other replicas use (scheme, host, port).
3. Tune `ForwarderRequestConfig` and `HttpClientConfig` for your downstream SLAs.
4. Use `DefaultAzureCredential` locally and deployment-specific credentials in production.
5. Monitor HybridCache hit rate and distributed cache availability for early warning.

### Minimal Redis Configuration

```csharp
using Azure.Identity;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;

var redisCredential = builder.Environment.IsDevelopment()
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var endpoint = builder.Configuration["Redis:Endpoint"]
    ?? throw new InvalidOperationException("Redis:Endpoint is required.");

var redisConfig = await ConfigurationOptions
    .Parse(endpoint)
    .ConfigureForAzureWithTokenCredentialAsync(redisCredential);

redisConfig.Ssl = true; // Always require TLS in production

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.ConfigurationOptions = redisConfig;
    options.InstanceName = "MCP:";
});

builder.Services.AddMcpHttpSessionAffinity(options =>
{
    options.LocalServerAddress = builder.Configuration["Server:InternalAddress"]
        ?? throw new InvalidOperationException("Server:InternalAddress is required.");
});
```

`appsettings.json`

```json
{
  "Redis": {
    "Endpoint": "your-mcp-session-affinity.region.redis.azure.net:6380"
  },
  "Server": {
    "InternalAddress": "http://pod-1.mcp.default.svc.cluster.local:8080"
  }
}
```

## Core Concepts

- Session ownership: the first request with `Mcp-Session-Id` (header) or `sessionId` (query) claims the session and stores ownership in HybridCache.
- HybridCache tiers: L1 memory cache plus optional L2 distributed cache; tune expiration to control how long ownership survives inactivity.
- Forwarding: if the current node is not the owner, YARP forwards the request to the owning instance over HTTP(S).
- Stale detection: when an owning instance restarts, the affinity entry is discarded so clients can establish a fresh session and rebuild state.

## Configuration Reference

- `SessionAffinityOptions.LocalServerAddress`: required in multi-instance environments; must be a routable absolute URI.
- `ForwarderRequestConfig`: controls forwarding timeout, buffering, and HTTP version.
- `HttpClientConfig`: tune connection pooling for heavy cross-node routing.
- `HybridCacheOptions`: set `DefaultEntryOptions.Expiration` (L2) and `LocalCacheExpiration` (L1) to balance freshness versus resilience.

## Observability

- Enable `ModelContextProtocol.AspNetCore.Distributed` logs at `Information` by default and `Debug` for routing traces.
- Watch for `ResolvingSessionOwner`, `SessionEstablished`, and `ForwardingRequest` events to understand ownership decisions.
- Export HybridCache hit/miss metrics to confirm cache sizing and detect unusual churn.
