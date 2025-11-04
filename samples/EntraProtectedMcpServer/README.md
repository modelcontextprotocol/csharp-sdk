# Protected MCP Server Sample with Microsoft Entra ID

This sample demonstrates how to create an MCP server that requires Microsoft Entra ID (Azure AD) OAuth 2.0 
authentication to access its tools and resources. The server provides weather-related tools, Microsoft Graph 
integration, and SharePoint tools protected by JWT bearer token authentication.

## Overview

The EntraProtectedMcpServer sample shows how to:
- Create an MCP server with Microsoft Entra ID OAuth 2.0 protection
- Configure JWT bearer token authentication with Azure AD
- Implement protected MCP tools and resources
- Integrate with ASP.NET Core authentication and authorization
- Provide OAuth resource metadata for client discovery
- Use On-Behalf-Of (OBO) flow for Microsoft Graph and SharePoint API access

## Features

- **Microsoft Entra ID Integration**: Full OAuth 2.0 authentication with Microsoft identity platform
- **Weather Tools**: National Weather Service API integration for alerts and forecasts
- **Microsoft Graph Tools**: Personalized user information and Graph API access
- **SharePoint Tools**: SharePoint Online REST API integration for site information
- **On-Behalf-Of Flow**: Secure token exchange for downstream API calls
- **User Secrets**: Secure storage of sensitive information during development

## Prerequisites

- .NET 9.0 or later
- Microsoft Entra ID tenant with registered applications
- Valid Entra ID credentials and API permissions configured
- Access to Microsoft Graph and SharePoint Online (if using those tools)

## Configuration

### Configuration Files

The server uses a layered configuration approach:

1. **appsettings.json** - Base configuration (non-sensitive)
2. **appsettings.Development.json** - Development overrides
3. **User Secrets** - Sensitive data (client secrets, credentials)
4. **Environment Variables** - Runtime overrides

### Configuration Structure

```json
{
  "Server": {
    "Url": "http://localhost:7071/",
    "ResourceDocumentationUrl": "https://docs.example.com/api/weather"
  },
  "EntraId": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "client-secret-from-user-secrets","
    "ScopesSupported": [
      "user_impersonation"
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "AllowedHosts": "*"
}
```


### Configuration Classes

The server uses strongly-typed configuration classes:

- **`ServerConfiguration`** - Server hosting and endpoint settings
- **`EntraIdConfiguration`** - Microsoft Entra ID authentication settings

## Setup and Running

### Step 1: Configure Microsoft Entra ID

1. Register an application in your Entra ID tenant for the MCP server
2. Configure API permissions:
   - **Microsoft Graph**: `User.Read` (for Graph tools)
   - **SharePoint**: `Sites.Read.All` or appropriate site permissions (for SharePoint tools)
3. Create a client secret for the On-Behalf-Of flow
4. Note down the Tenant ID and Client ID

### Step 2: Update Configuration

1. Update `appsettings.json` with your tenant and application IDs
2. Set the client secret using user secrets (see above)
3. Optionally configure external API settings

### Step 3: Run the Protected MCP Server

## Running the Server

### Locally with Visual Studio

1. Press F5 to build and run the server
2. The server will start at `http://localhost:7071`

### Docker

To run the server in a Docker container:

```bash
docker build -t entra-protected-mcp-server .
docker run -d -p 7071:80 --name mcp-server entra-protected-mcp-server
```

## Testing the Server

### With Protected MCP Client

Use the ProtectedMcpClient sample to test the server:

```bash
cd samples\ProtectedMcpClient
dotnet run
```


## Testing Without Client

You can test the server directly using HTTP tools:

1. Get an access token from Microsoft Entra ID with appropriate scopes
2. Include the token in the `Authorization: Bearer <token>` header
3. Make requests to the MCP endpoints:
   - `POST http://localhost:7071/` with MCP JSON-RPC requests
   - `GET http://localhost:7071/.well-known/oauth-protected-resource` for OAuth metadata

### Example MCP Request

```
curl -X POST http://localhost:7071/ 
-H "Authorization: Bearer YOUR_ACCESS_TOKEN" 
-H "Content-Type: application/json" 
-H "ProtocolVersion: 2025-06-18" 
-d '{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "hello" } }'
```

## What the Server Provides

### Protected Resources

- **MCP Endpoint**: `http://localhost:7071/` (requires authentication)
- **OAuth Resource Metadata**: `http://localhost:7071/.well-known/oauth-protected-resource`

### Available Tools

The server provides several categories of protected tools:

#### Weather Tools (WeatherTools.cs)
1. **GetAlerts**: Get weather alerts for a US state
   - Parameter: `state` (string) - 2-letter US state abbreviation
   - Example: `GetAlerts` with `state: "WA"`
   - Data source: National Weather Service API

2. **GetForecast**: Get weather forecast for a location
   - Parameters: 
     - `latitude` (double) - Latitude coordinate
     - `longitude` (double) - Longitude coordinate
   - Example: `GetForecast` with `latitude: 47.6062, longitude: -122.3321`
   - Data source: National Weather Service API

#### Microsoft Graph Tools (GraphTools.cs)
1. **Hello**: Get a personalized greeting using Microsoft Graph
   - No parameters required
   - Uses On-Behalf-Of flow to access Microsoft Graph
   - Retrieves user's display name from `/me` endpoint
   - Requires: `User.Read` permission

#### SharePoint Tools (SPOTools.cs)
1. **GetSiteInfo**: Get SharePoint site information
   - Parameter: `siteUrl` (string) - SharePoint site URL
   - Example: `GetSiteInfo` with `siteUrl: "https://contoso.sharepoint.com/sites/sitename"`
   - Uses On-Behalf-Of flow to access SharePoint REST API
   - Returns site title, description, URL, creation date, and language
   - Requires: Appropriate SharePoint permissions

### Authentication & Authorization Flow

1. **Initial Authentication**: Client authenticates with Entra ID and receives an access token
2. **MCP Request**: Client includes the access token in the `Authorization: Bearer` header
3. **Token Validation**: Server validates the JWT token against Entra ID
4. **On-Behalf-Of Flow**: For Graph/SharePoint tools, server exchanges the user's token for downstream API tokens
5. **API Access**: Server uses the exchanged tokens to call Microsoft Graph or SharePoint APIs
6. **Response**: Server returns the processed results to the client

## Architecture

The server uses:
- **ASP.NET Core** for hosting and HTTP handling
- **JWT Bearer Authentication** for Microsoft Entra ID token validation
- **MCP Authentication Extensions** for OAuth resource metadata
- **On-Behalf-Of (OBO) Flow** for secure token exchange
- **HttpClientFactory** for calling external APIs (Weather.gov, Microsoft Graph, SharePoint)
- **Authorization** to protect MCP endpoints
- **Strongly-typed Configuration** for settings management

## Configuration Details

### Default Settings
- **Server URL**: `http://localhost:7071`
- **Resource Documentation**: `https://docs.example.com/api/weather`
- **Weather API**: `https://api.weather.gov`
- **Microsoft Graph API**: `https://graph.microsoft.com`

### Required Permissions
- **Microsoft Graph**: `User.Read`
- **SharePoint**: `Sites.Read.All` or site-specific permissions
- **MCP Server**: `user_impersonation` scope


## External Dependencies

- **National Weather Service API** (`api.weather.gov`): Real weather data
- **Microsoft Graph API** (`graph.microsoft.com`): User information and Microsoft 365 data
- **SharePoint REST API**: SharePoint Online site and content access
- **Microsoft Entra ID**: Authentication and token services

## Troubleshooting

### Configuration Issues
- Verify all required configuration sections are present in `appsettings.json`
- Check user secrets are properly configured: `dotnet user-secrets list --project samples/EntraProtectedMcpServer`
- Ensure client secret is set for On-Behalf-Of flow

### Authentication Issues
- Ensure the ASP.NET Core dev certificate is trusted:

```
dotnet dev-certs https --clean dotnet dev-certs https --trust
```

- Verify Entra ID application registrations and permissions
- Check token audience and issuer validation settings
- Ensure API permissions are granted and admin consented

### On-Behalf-Of Flow Issues
- Verify the client application has the necessary delegated permissions
- Check that the client secret is correctly configured
- Ensure the original access token has the required scopes
- Verify the target APIs (Graph/SharePoint) are accessible

### Network Issues
- Check that port 7071 is available and not blocked by firewall
- Verify external API endpoints are accessible (weather.gov, graph.microsoft.com)
- Check proxy settings if behind a corporate firewall

### API Permission Issues
- Ensure Microsoft Graph permissions are granted: `User.Read`
- Verify SharePoint permissions are appropriate for the target sites
- Check that admin consent has been provided for application permissions

## Security Considerations

- **Client Secrets**: Never commit client secrets to source control. Always use user secrets for development and secure storage (Key Vault) for production
- **Token Scoping**: Request only the minimum necessary permissions/scopes
- **On-Behalf-Of Flow**: Tokens are exchanged securely and not stored persistently
- **HTTPS**: Use HTTPS in production environments
- **Token Validation**: All tokens are properly validated against Microsoft Entra ID

## Key Files

- **`Program.cs`**: Server setup with authentication and MCP configuration
- **`Configuration/`**: Strongly-typed configuration classes
- `ServerConfiguration.cs`: Server hosting settings
- `EntraIdConfiguration.cs`: Entra ID authentication settings
- `ExternalApiConfiguration.cs`: External API configurations
- **`Tools/`**: MCP tool implementations
- `WeatherTools.cs`: Weather-related tools using weather.gov API
- `GraphTools.cs`: Microsoft Graph integration with On-Behalf-Of flow
- `SPOTools.cs`: SharePoint Online REST API integration
- **`appsettings.json`**: Base application configuration
- **`EntraProtectedMcpServer.csproj`**: Project file with dependencies and user secrets

## Related Samples

- **EntraProtectedMcpClient**: Corresponding client implementation with Entra ID
- **ProtectedMcpClient**: Alternative client using test OAuth server
- **ProtectedMcpServer**: Server using test OAuth server instead of Entra ID

## Additional Resources

- [Microsoft Entra ID Documentation](https://docs.microsoft.com/azure/active-directory/)
- [On-Behalf-Of Flow](https://docs.microsoft.com/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow)
- [Microsoft Graph API](https://docs.microsoft.com/graph/)
- [SharePoint REST API](https://docs.microsoft.com/sharepoint/dev/sp-add-ins/get-to-know-the-sharepoint-rest-service)
- [ASP.NET Core Authentication](https://docs.microsoft.com/aspnet/core/security/authentication/)
- [.NET Configuration](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/)