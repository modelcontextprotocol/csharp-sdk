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

- .NET 9.0 or later
- A running TestOAuthServer (for OAuth authentication)

## Setup and Running

### Step 1: Start the Test OAuth Server

First, you need to start the TestOAuthServer which issues access tokens:

```bash
cd tests\ModelContextProtocol.TestOAuthServer
dotnet run --framework net9.0
```

The OAuth server will start at `https://localhost:7029`, on the ASP.NET Core developer certificate.
Run `dotnet dev-certs https --trust` once if you have not already.

If your MCP client cannot fetch the metadata from that certificate - see [Step 4](#step-4-test-with-an-editor) -
start the authorization server over plain loopback HTTP instead and point this sample at it:

```bash
# terminal 1
dotnet run --framework net9.0 -- --http
# terminal 2
dotnet run --OAuth:ServerUrl=http://localhost:7029
```

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

### Step 4: Test with an editor

Add `http://localhost:7071/` as an HTTP MCP server in VS Code (or any other MCP client). The client
gets a 401 with `WWW-Authenticate`, reads the protected resource metadata, discovers the
authorization server at `http://localhost:7029`, registers itself through Dynamic Client
Registration, and completes the code flow in the browser.

VS Code does not use the operating system trust store for these requests, so with the default HTTPS
authorization server the metadata fetch fails even after `dotnet dev-certs https --trust`, and the
fallback for pre-2025-06-18 servers kicks in: it asks for a client ID because it no longer knows
about the registration endpoint, then sends the browser to `http://localhost:7071/authorize`, which
404s. Use the `--http` pair of commands from Step 1 for those clients.

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
- Accept JWT bearer tokens from the OAuth server at `https://localhost:7029`, overridable with `OAuth:ServerUrl`
- Validate token audience as `demo-client`
- Require tokens to have appropriate scopes (`mcp:tools`)
- Provide OAuth resource metadata for client discovery

`JwtBearerOptions.RequireHttpsMetadata` follows the scheme of that authority, so it stays at its
default of `true` unless you have deliberately pointed the sample at a plain-HTTP loopback address.
Never relax it for an authority you do not fully control on the local machine: it lets the OpenID
Connect metadata and the token signing keys be fetched over an unprotected connection.

## Architecture

The server uses:
- **ASP.NET Core** for hosting and HTTP handling
- **JWT Bearer Authentication** for token validation
- **MCP Authentication Extensions** for OAuth resource metadata
- **HttpClient** for calling the weather.gov API
- **Authorization** to protect MCP endpoints

## Configuration Details

- **Server URL**: `http://localhost:7071`
- **OAuth Server**: `http://localhost:7029`
- **Demo Client ID**: `demo-client`

## Testing Without Client

You can test the server directly using HTTP tools:

1. Get an access token from the OAuth server
2. Include the token in the `Authorization: Bearer <token>` header
3. Make requests to the MCP endpoints

## External Dependencies

The weather tools use the National Weather Service API at `api.weather.gov` to fetch real weather data.

## Troubleshooting

- If you run the TestOAuthServer with `--https`, ensure the ASP.NET Core dev certificate is trusted.
  ```
  dotnet dev-certs https --clean
  dotnet dev-certs https --trust
  ```
- Ensure the TestOAuthServer is running first
- Check that port 7071 is available
- Verify the OAuth server is accessible at `http://localhost:7029`
- Check console output for authentication events and errors

## Key Files

- `Program.cs`: Server setup with authentication and MCP configuration
- `Tools/WeatherTools.cs`: Weather tool implementations
- `Tools/HttpClientExt.cs`: HTTP client extensions
- `Properties/launchSettings.json`: Development launch configuration