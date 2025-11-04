# Protected MCP Client Sample with Microsoft Entra ID

This sample demonstrates how to create an MCP client that connects to a protected MCP server using Microsoft Entra ID (Azure AD) OAuth 2.0 authentication. The client implements a custom OAuth authorization flow with browser-based authentication and showcases externalized configuration management.

## Overview

The EntraProtectedMcpClient sample shows how to:
- Connect to an OAuth-protected MCP server using Microsoft Entra ID
- Handle OAuth 2.0 authorization code flow with PKCE
- Use custom authorization redirect handling with local HTTP listener
- Call protected MCP tools with authentication
- Manage sensitive data using user secrets
- Test access to Graph and SharePoint

## Features

- **Microsoft Entra ID Integration**: Full OAuth 2.0 authentication with Microsoft identity platform
- **User Secrets**: Secure storage of sensitive information during development
- **Graph and SharePoint Access**: Test authentication through MS Graph and SharePoint
- **Comprehensive Logging**: Structured logging with configurable levels

## Prerequisites

- .NET 9.0 or later
- Microsoft Entra ID tenant with registered applications
- A running EntraProtectedMcpServer (for MCP services)
- Valid Entra ID credentials configured

## Configuration

### Configuration Files

The client uses a layered configuration approach:

1. **appsettings.json** - Base configuration (non-sensitive)
2. **appsettings.Development.json** - Development overrides
3. **User Secrets** - Sensitive data (client secrets, credentials)
4. **Environment Variables** - Runtime overrides

### Configuration Structure

```json
{
  "McpServer": {
    "Url": "http://localhost:7071/"
  },
  "EntraId": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ServerClientId": "server-app-registration-id",
    "RedirectUri": "http://localhost:1179/callback",
    "Scope": "user_impersonation",
    "ResponseMode": "query"
  },
  "SecuredSpoSite": {
    "Url": "https://docs.microsoft.com/"
  }
}
```

### Setting Up User Secrets

Store sensitive information securely using .NET user secrets:

```bash
dotnet user-secrets set "McpClient:EntraId:ClientSecret" "your-client-secret"
```

### Configuration Classes

The client uses strongly-typed configuration classes:

- **`McpClientConfiguration`** - MCP server connection settings
- **`EntraIdConfiguration`** - Microsoft Entra ID authentication settings
- **`SecuredSpoSiteConfiguration`** - SharePoint site configuration for testing

## Setup and Running

### Step 1: Configure Microsoft Entra ID

1. Register two applications in your Entra ID tenant:
   - **Client Application** (this sample)
   - **Server Application** (for the MCP server)

2. Configure the client application:
   - Set redirect URI: `http://localhost:1179/callback`
   - Enable public client flows if needed
   - Grant necessary API permissions

3. Configure the server application:
   - Expose an API with scopes (e.g., `user_impersonation`)
   - Configure authentication settings

### Step 2: Update Configuration

1. Update `appsettings.json` with your tenant and application IDs
2. Set the client secret using user secrets (see above)
3. Optionally configure different SharePoint sites for testing

### Step 3: Start the Protected MCP Server

Start the EntraProtectedMcpServer which provides the protected tools:

The protected server will start at `http://localhost:7071`

### Step 4: Run the Protected MCP Client

Run this client:

```bash
dotnet run
```

```plaintext
Note: Ensure you have the necessary dependencies and runtimes installed.
```

## What Happens

1. **Configuration Loading**: The client loads configuration from multiple sources (files, user secrets, environment variables)
2. **Configuration Validation**: Validates required settings and displays helpful error messages
3. **Server Connection**: Attempts to connect to the protected MCP server at the configured URL
4. **OAuth Discovery**: The server responds with OAuth metadata indicating authentication is required
5. **Authentication Flow**: The client initiates Microsoft Entra ID OAuth 2.0 authorization code flow:
   - Opens a browser to the Entra ID authorization URL
   - Starts a local HTTP listener on the configured redirect URI
   - Exchanges the authorization code for an access token using PKCE
6. **API Access**: The client uses the access token to authenticate with the MCP server
7. **Tool Execution**: Lists available tools and demonstrates calling various protected tools:
   - Weather alerts for Washington state
   - Hello/greeting tools
   - SharePoint site information retrieval

## Available Tools

Once authenticated, the client can access protected tools including:

- **get_alerts**: Get weather alerts for a US state
- **get_forecast**: Get weather forecast for a location (latitude/longitude)  
- **hello**: Simple greeting tool for testing using ("https://graph.microsoft.com/v1.0/me")
- **get_site_info**: Retrieve SharePoint site information (requires appropriate permissions)

## Troubleshooting

### Authentication Issues
- Ensure the ASP.NET Core dev certificate is trusted:
- Verify Entra ID application registrations and permissions
- Check that the client secret is correctly configured in user secrets
- Ensure the redirect URI matches exactly in both the code and Entra ID app registration

### Configuration Issues
- Run with debug logging to see configuration loading details
- Verify all required configuration sections are present
- Check user secrets are properly configured: `dotnet user-secrets list --project samples/EntraProtectedMcpClient`

### Connection Issues
- Ensure the EntraProtectedMcpServer is running and accessible
- Check that ports 7071 and 1179 are available
- Verify firewall settings allow the required connections

### Browser Issues
- If the browser doesn't open automatically, copy the authorization URL from the console
- Allow/trust the OAuth server's certificate in your browser
- Clear browser cache/cookies if experiencing authentication issues

## Security Considerations

- **Client Secrets**: Never commit client secrets to source control. Always use user secrets for development and secure storage (Key Vault) for production
- **Redirect URIs**: Ensure redirect URIs are exactly configured in both the application and Entra ID
- **Scopes**: Request only the minimum necessary permissions/scopes
- **Token Storage**: The client handles token storage securely in memory only

## Key Files

- **`Program.cs`**: Main client application with OAuth flow implementation and configuration management
- **`EntraProtectedMcpClient.csproj`**: Project file with dependencies and user secrets configuration
- **`appsettings.json`**: Base application configuration (non-sensitive)
- **`Configuration/`**: Strongly-typed configuration classes
- `McpClientConfiguration.cs`: MCP server settings
- `EntraIdConfiguration.cs`: Entra ID authentication settings
- `SecuredSpoSiteConfiguration.cs`: SharePoint site configuration

## Related Samples

- **EntraProtectedMcpServer**: The corresponding server implementation
- **ProtectedMcpClient**: Alternative client using test OAuth server
- **ProtectedMcpServer**: Server using test OAuth server

## Additional Resources

- [Microsoft Entra ID Documentation](https://docs.microsoft.com/azure/active-directory/)
- [OAuth 2.0 Authorization Code Flow](https://docs.microsoft.com/azure/active-directory/develop/v2-oauth2-auth-code-flow)
- [.NET Configuration Documentation](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/)
- [User Secrets in Development](https://docs.microsoft.com/aspnet/core/security/app-secrets)