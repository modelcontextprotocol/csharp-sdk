---
title: Docker Deployment
author: jeffhandley
description: How to run ASP.NET Core MCP servers in Docker containers using Streamable HTTP transport.
uid: docker-deployment
---

## Docker deployment for MCP servers

Docker is a practical way to package and run MCP servers consistently across development, CI, and production environments. For HTTP-based MCP servers, use ASP.NET Core hosting with Streamable HTTP.

This guide assumes you already have an ASP.NET Core MCP server configured with `ModelContextProtocol.AspNetCore`, `WithHttpTransport()`, and `MapMcp()`.

<!-- mlc-disable-next-line -->

> [!TIP]
> For local, process-based integrations where the client launches the server directly, stdio is often simpler. For remote and containerized deployments, Streamable HTTP is the recommended transport.

### Baseline server

A minimal HTTP-based MCP server looks like this:

```csharp
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");
app.Run();
```

### Dockerfile

Use a multi-stage Docker build so SDK tooling stays in the build stage and only runtime dependencies are shipped in the final image.

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# MCP HTTP endpoint listens on 8080 inside container
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyMcpServer.dll"]
```

Replace `MyMcpServer.dll` with your server assembly output name.

### Build and run

Build the image:

```bash
docker build -t my-mcp-server:latest .
```

Run the container and map host port `3001` to container port `8080`:

```bash
docker run --rm -p 3001:8080 my-mcp-server:latest
```

With the baseline route above (`app.MapMcp("/mcp")`), clients connect to:

- Streamable HTTP: `http://localhost:3001/mcp`
- Legacy SSE endpoint (if needed): `http://localhost:3001/mcp/sse`

### Configuration and secrets

Pass configuration through environment variables rather than baking secrets into the image:

```bash
docker run --rm -p 3001:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e MYAPP__APIKEY=example \
  my-mcp-server:latest
```

ASP.NET Core binds `MYAPP__APIKEY` to `MYAPP:APIKEY` in configuration.

<!-- mlc-disable-next-line -->

> [!IMPORTANT]
> Do not commit real tokens or credentials into Dockerfiles, compose files, or source code. Use runtime environment variables or an external secret store.

### Health and readiness

For container orchestrators, add an HTTP health endpoint and use it for readiness/liveness checks. Keep MCP traffic on your mapped MCP route and health probes on a separate route.

### Reverse proxies and forwarded headers

If your container is behind a reverse proxy (for example, ingress or load balancers), ensure forwarded headers are handled correctly so auth and origin metadata flow to the MCP server as expected.

See also:

- [Transports](../transports/transports.md)
- [Getting Started](../getting-started.md)
- [HTTP Context](../httpcontext/httpcontext.md)
