# Protected MCP Server Sample

This sample demonstrates how to create an MCP server that requires OAuth 2.0 authentication to access its tools and resources. The server provides weather-related tools protected by JWT bearer token authentication.

## Overview

The Protected MCP Server sample shows how to:
- Create an MCP server with OAuth 2.0 protection
- Configure JWT bearer token authentication
- Implement protected MCP tools and resources
- Integrate with ASP.NET Core authentication and authorization
- Provide OAuth resource metadata for client discovery

## Prerequisites

- .NET 10 SDK to build and run this sample
- .NET 9 runtime when using TestOAuthServer with the command below
- A running TestOAuthServer, or an external authorization server such as Microsoft Entra ID

## Setup and Running

### Step 1: Start the Test OAuth Server

First, you need to start the TestOAuthServer which issues access tokens:

```bash
cd tests\ModelContextProtocol.TestOAuthServer
dotnet run --framework net9.0
```

The OAuth server will start at `https://localhost:7029`

### Step 2: Start the Protected MCP Server

Run this protected server:

```bash
cd samples\ProtectedMcpServer
dotnet run
```

The protected server will start at `http://localhost:7071`

### Step 3: Test with Protected MCP Client

You can test the server using the ProtectedMcpClient sample:

```bash
cd samples\ProtectedMcpClient
dotnet run
```

## What the Server Provides

### Protected Resources

- **MCP Endpoint**: `http://localhost:7071/` (requires authentication)
- **OAuth Resource Metadata**: `http://localhost:7071/.well-known/oauth-protected-resource`

### Available Tools

The server provides weather-related tools that require authentication:

1. **GetAlerts**: Get weather alerts for a US state
   - Parameter: `state` (string) - 2-letter US state abbreviation
   - Example: `GetAlerts` with `state: "WA"`

2. **GetForecast**: Get weather forecast for a location
   - Parameters: 
     - `latitude` (double) - Latitude coordinate
     - `longitude` (double) - Longitude coordinate
   - Example: `GetForecast` with `latitude: 47.6062, longitude: -122.3321`

### Authentication Configuration

The server is configured to:
- Accept JWT bearer tokens from the OAuth server at `https://localhost:7029`
- Validate token audience as `http://localhost:7071`
- Require the `mcp:tools` scope in the token's `scope` or `scp` claim
- Provide OAuth resource metadata for client discovery

## Architecture

The server uses:
- **ASP.NET Core** for hosting and HTTP handling
- **JWT Bearer Authentication** for token validation
- **MCP Authentication Extensions** for OAuth resource metadata
- **HttpClient** for calling the weather.gov API
- **Authorization** to protect MCP endpoints

## Configuration

The defaults in `appsettings.json` work with TestOAuthServer. Override them using ASP.NET Core configuration: environment variables, command-line arguments, development user secrets, or an environment-specific settings file.

| Key | Default | Purpose |
| --- | --- | --- |
| `Urls` | `http://localhost:7071` | Listening address. Override with the `Urls` environment variable or `--urls`. |
| `Mcp:Resource` | `http://localhost:7071` | Exact canonical resource identifier published in protected resource metadata and sent by clients as `resource`. |
| `Authentication:Authority` | `https://localhost:7029` | Authorization server issuer, used for JWT validation and advertised to clients. |
| `Authentication:Audience` | `http://localhost:7071` | Expected JWT `aud` claim. This is the server application's client ID GUID for Entra v2 tokens. |
| `Authentication:ScopesSupported` | `["mcp:tools"]` | Scopes advertised to clients. For Entra, use fully qualified scope URLs. |
| `Authentication:RequiredScopes` | `["mcp:tools"]` | Scope values required in the token's space-delimited `scp` or `scope` claim. All configured scopes are required. Entra uses short scope names here. |
| `Mcp:AllowedOrigins` | `["http://localhost:5173"]` | Browser origins allowed by CORS. This is not a list of OAuth redirect URIs. |

Use `__` instead of `:` for environment variable keys, and numeric indices for array elements, for example `Authentication__ScopesSupported__0`.

### Microsoft Entra ID

Start with Microsoft's [Secure a Model Context Protocol (MCP) server with Microsoft Entra ID](https://learn.microsoft.com/en-us/entra/agent-id/secure-mcp-server-with-entra-id). It describes the resource-indicator configuration; no SDK change to remove `resource` is needed. A community [C# SDK OAuth walkthrough](https://github.com/pdebruin/mcpoauth/blob/main/walkthrough.md) also illustrates server and Inspector setup. Follow Microsoft's explicit token-version step even if another walkthrough omits it.

#### Register the API

1. Create a single-tenant app registration for the MCP server. Record its **Directory (tenant) ID** and **Application (client) ID**.
2. Set `api.requestedAccessTokenVersion` to `2` in its manifest **before** changing the Application ID URI.
3. Set the **Application ID URI** to `https://localhost:7071`, matching `Mcp:Resource` exactly. Do not add a trailing slash. If you use another HTTPS hostname or port, change both the registration and sample configuration.
4. Expose a delegated scope named `mcp:tools`. Its fully qualified value is `https://localhost:7071/mcp:tools`.

The server validates tokens; it does not sign users in or redeem authorization codes. It does **not** need a client secret or a redirect URI.

#### Configure and run the server

Trust the development certificate if necessary with `dotnet dev-certs https --trust`. From the repository root, run these PowerShell commands, replacing the two placeholders:

```powershell
$env:Authentication__Authority = "https://login.microsoftonline.com/<tenant-id>/v2.0"
$env:Authentication__Audience = "<server-application-client-id>"
$env:Authentication__ScopesSupported__0 = "https://localhost:7071/mcp:tools"
$env:Authentication__RequiredScopes__0 = "mcp:tools"
$env:Mcp__Resource = "https://localhost:7071"

dotnet run --project samples\ProtectedMcpServer --no-launch-profile --urls https://localhost:7071
```

Use the tenant GUID in `Authority` so it matches the v2 token issuer. The resource URL and audience GUID are intentionally different. The middleware still validates the signature, issuer, audience, and lifetime; the authorization policy additionally requires the delegated scope. App-only tokens with only a `roles` claim do not satisfy this sample's delegated-access policy.

For a deployed server, use its externally reachable HTTPS URL as the resource and Application ID URI, and configure its allowed hosts and browser origins appropriately. Do not disable issuer, audience, or HTTPS metadata validation.

#### Register and configure clients

Entra's discovery document does not advertise Dynamic Client Registration (DCR) or Client ID Metadata Documents (CIMD). Use a pre-registered client rather than relying on automatic registration:

1. Create a separate single-tenant **public client** registration for the test client.
2. Under **Authentication**, add a **Mobile and desktop applications** redirect URI matching the client's callback.
3. Add the MCP API's delegated `mcp:tools` permission. Complete user/admin consent as required by the tenant. Optionally pre-authorize that client for this scope under the server app's **Expose an API** settings.
4. Supply the **client** application's ID to the MCP client, not the server application's ID. Public clients use authorization code flow with PKCE and no client secret.

Client support and configuration differ:

| Client | Configuration |
| --- | --- |
| VS Code | Supports a pre-registered ID through `oauth.clientId` in its MCP server configuration. Register the callback used by your VS Code version. Its Microsoft authentication integration can alternatively use the VS Code first-party application; see [Microsoft's VS Code setup guide](https://learn.microsoft.com/en-us/azure/app-service/configure-authentication-mcp-server-vscode#authorize-visual-studio-code-in-the-app-registration) for pre-authorization. The App Service hosting steps are not needed for this sample. |
| Claude Code | `claude mcp add --transport http --client-id <client-id> --callback-port 1180 weather https://localhost:7071`. Register `http://localhost:1180/callback`, then authenticate from `/mcp`. See [pre-configured OAuth credentials](https://code.claude.com/docs/en/mcp#use-pre-configured-oauth-credentials). |
| Codex | Supports pre-registered clients through `codex mcp add weather --url https://localhost:7071 --oauth-client-id <client-id>`. Register the **exact callback URL the command prints**, then run `codex mcp login weather`. See [OAuth client registration and callbacks](https://developers.openai.com/codex/mcp#oauth-client-registration-and-callbacks). Older versions may lack these options. |
| ProtectedMcpClient | Its current defaults use DCR and `http://localhost:7071/`. For a pre-registered client, set `ClientOAuthOptions.ClientId` and use the HTTPS endpoint; register `http://localhost:1179/callback`. Also see the discovery limitation below. |

**Current C# client discovery limitation:** Entra's v2 OpenID discovery document currently omits `code_challenge_methods_supported`, even though Entra supports PKCE. The C# SDK requires metadata to advertise `S256` and rejects that document before starting interactive OAuth. Changing this server's settings or supplying a client ID does not resolve that separate limitation. The legacy metadata fallback switch does not bypass PKCE validation of discovered metadata. Do not remove the `resource` parameter or fabricate authorization server capabilities as a workaround. A token obtained separately through a trusted Entra-aware authentication library can still be sent as a bearer token to this server.

The HTTP transport and bearer-token validation are client-independent, but successful automatic sign-in also depends on each client's discovery checks, static-registration support, callback matching, certificate trust, and tenant consent policies. The client examples above are setup guidance, not a claim that every client's complete Entra flow has been verified.

#### Check discovery

```powershell
curl.exe -i --request POST https://localhost:7071/
curl.exe https://localhost:7071/.well-known/oauth-protected-resource
```

The first request should return `401` with a `WWW-Authenticate` header containing `resource_metadata`. The metadata should contain:

```json
{
  "resource": "https://localhost:7071",
  "authorization_servers": [
    "https://login.microsoftonline.com/<tenant-id>/v2.0"
  ],
  "scopes_supported": [
    "https://localhost:7071/mcp:tools"
  ],
  "bearer_methods_supported": ["header"]
}
```

A valid delegated access token for this API with `scp` containing `mcp:tools` authorizes MCP requests. A wrong-audience token returns `401`; an authenticated token missing the required scope returns `403`.

## Testing Without Client

You can test the server directly using HTTP tools:

1. Get an access token from the OAuth server
2. Include the token in the `Authorization: Bearer <token>` header
3. Make requests to the MCP endpoints

## External Dependencies

The weather tools use the National Weather Service API at `api.weather.gov` to fetch real weather data.

## Troubleshooting

- Ensure the ASP.NET Core dev certificate is trusted.
  ```
  dotnet dev-certs https --clean
  dotnet dev-certs https --trust
  ```
- Ensure the TestOAuthServer is running first
- Check that port 7071 is available
- Verify the OAuth server is accessible at `https://localhost:7029`
- Check console output for authentication events and errors

## Key Files

- `Program.cs`: Server setup with authentication and MCP configuration
- `Tools/WeatherTools.cs`: Weather tool implementations
- `Tools/HttpClientExt.cs`: HTTP client extensions
- `Properties/launchSettings.json`: Development launch configuration