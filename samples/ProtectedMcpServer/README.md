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
- Validate token audience as `demo-client`
- Require tokens to have appropriate scopes (`mcp:tools`)
- Provide OAuth resource metadata for client discovery

## Architecture

The server uses:
- **ASP.NET Core** for hosting and HTTP handling
- **JWT Bearer Authentication** for token validation
- **MCP Authentication Extensions** for OAuth resource metadata
- **HttpClient** for calling the weather.gov API
- **Authorization** to protect MCP endpoints

## Configuration Details

- **Server URL**: `http://localhost:7071`
- **OAuth Server**: `https://localhost:7029`
- **Demo Client ID**: `demo-client`

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