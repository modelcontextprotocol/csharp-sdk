# Azure B2C Client Credentials MCP Server

This sample demonstrates how to create an MCP (Model Context Protocol) server that requires OAuth 2.0 Client Credentials authentication using Azure B2C to protect its tools and resources.

> **⚠️ Important Note**: Azure B2C requires a policy/user flow even for client credentials flow, which is different from Azure AD. This is a key architectural difference between Azure B2C (consumer identity) and Azure AD (enterprise identity).

## Overview

The Azure B2C Client Credentials MCP Server shows how to:
- Create an MCP server with Azure B2C OAuth 2.0 protection
- Configure JWT bearer token authentication with Azure B2C
- Implement protected MCP tools and resources
- Integrate with ASP.NET Core authentication and authorization
- Provide OAuth resource metadata for client discovery

## Prerequisites

- .NET 9.0 or later
- Azure B2C tenant
- VSCode with REST Client extension (for testing)

## Azure B2C Setup

### 1. Create Azure B2C Tenant

1. Navigate to the Azure portal
2. Create a new Azure AD B2C tenant
3. Note your tenant name (e.g., `yourtenant.onmicrosoft.com`)

### 2. Register Application

1. In your B2C tenant, go to **App registrations**
2. Click **New registration**
3. Configure:
   - **Name**: MCP Server API
   - **Supported account types**: Accounts in any identity provider or organizational directory
   - **Redirect URI**: Leave blank (not needed for client credentials flow)
4. After creation, note the **Application (client) ID**

### 3. Create User Flow (Required for Azure B2C)

**IMPORTANT**: Unlike Azure AD, Azure B2C requires a policy/user flow even for client credentials flow. This is a key difference between Azure B2C and Azure AD.

**For Azure B2C Client Credentials Flow:**
1. Go to **User flows** in your B2C tenant
2. Click **New user flow**
3. Select **Sign up and sign in**
4. Choose **Recommended** version
5. Name it `B2C_1_signupsignin` (or your preferred name)
6. Configure the user flow (even though it won't be used for user interaction)

**Note**: While the user flow won't be used for actual user sign-in (since this is machine-to-machine authentication), Azure B2C's architecture requires the policy context for token issuance. This is different from Azure AD which supports policy-free client credentials flow.

### 4. Generate Client Secret

1. Go to **Certificates & secrets** in your app registration
2. Click **New client secret**
3. Add a description and set expiration
4. Copy the secret value (you won't be able to see it again)

### 5. Configure API Permissions (Optional)

For client credentials flow, you may want to:
1. Go to **API permissions** in your app registration
2. Add any required permissions for your application
3. Grant admin consent if needed

## Configuration

Update the configuration in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.AspNetCore.Authentication": "Debug"
    }
  },
  "AzureB2C": {
    "Instance": "https://yourtenant.b2clogin.com",
    "Tenant": "yourtenant.onmicrosoft.com",
    "Policy": "B2C_1A_SIGNUP_SIGNIN",
    "ClientId": "your-actual-client-id"
  }
}
```

**Important Notes:**
- Replace `yourtenant` with your actual B2C tenant name
- Replace `your-actual-client-id` with your application's client ID
- **Policy is Required**: Azure B2C requires the policy even for client credentials flow (unlike Azure AD)
- The `Policy` should match the user flow you created (e.g., `B2C_1_signupsignin`) or custom policy (e.g., `B2C_1A_SIGNUP_SIGNIN`)
- Client secrets are not stored in configuration files for security reasons - they should be provided via environment variables or Azure Key Vault in production

## Running the Server

1. Update the Azure B2C configuration in `appsettings.Development.json`
2. Run the server:
   ```bash
   dotnet run
   ```
3. The server will start at `http://localhost:7071/`

## What the Server Provides

### Protected Resources

- **MCP Endpoint**: `http://localhost:7071/` (requires authentication)
- **OAuth Resource Metadata**: `http://localhost:7071/.well-known/oauth-protected-resource`

### Available Tools

The server provides weather-related tools that require authentication:

1. **get_alerts**: Get weather alerts for a US state
   - Parameter: `state` (string) - 2-letter US state abbreviation
   - Example: `get_alerts` with `state: "WA"`

2. **get_forecast**: Get weather forecast for a location
   - Parameters: 
     - `latitude` (number) - Latitude coordinate
     - `longitude` (number) - Longitude coordinate
   - Example: `get_forecast` with `latitude: 47.6062, longitude: -122.3321`

> **Note**: Tool names follow the MCP convention of snake_case. The C# method names `GetAlerts` and `GetForecast` are automatically converted to `get_alerts` and `get_forecast` respectively.

## Testing the Server

### Using REST Client Extension

1. Install the REST Client extension in VSCode
2. Use the included `test-azure-b2c.http` file to test the API:

```http
### Get Azure B2C Token
POST https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1A_SIGNUP_SIGNIN/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials
&client_id=your-client-id
&client_secret=your-client-secret
&scope=https://yourtenant.onmicrosoft.com/your-client-id/.default

### Test MCP Server Metadata (replace {{token}} with actual token from above)
GET http://localhost:7071/.well-known/oauth-protected-resource
Authorization: Bearer {{token}}

### Test MCP Tools List
POST http://localhost:7071/
Authorization: Bearer {{token}}
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}

### Test Weather Forecast Tool
POST http://localhost:7071/
Authorization: Bearer {{token}}
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_forecast",
    "arguments": {
      "latitude": 47.6062,
      "longitude": -122.3321
    }
  }
}
```

**Important Notes:**
- Replace `yourtenant` with your actual B2C tenant name
- Replace `your-client-id` with your application's client ID
- Replace `your-client-secret` with your application's client secret
- The scope format is `https://yourtenant.onmicrosoft.com/your-client-id/.default`
- Copy the `access_token` from the first request and use it in subsequent requests

## Authentication Flow

1. **Client Credentials Grant**: Client (application) requests token from Azure B2C using client credentials (client ID and client secret)
2. **Token Request**: POST to `https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1A_SIGNUP_SIGNIN/oauth2/v2.0/token`
3. **Token Response**: Azure B2C returns JWT access token (note: policy is required in URL even for client credentials)
4. **API Call**: Client includes token in Authorization header: `Bearer <token>`
5. **Token Validation**: MCP Server validates token against Azure B2C's public keys
6. **Access Granted**: If valid, MCP tools are accessible

**Key Points:**
- **Policy Required**: Azure B2C requires a policy in the URL even for client credentials flow (unlike Azure AD)
- No user interaction required (server-to-server authentication)
- Client must be registered in Azure B2C
- Client secret must be kept secure
- Tokens have expiration times and should be refreshed as needed

## Architecture

The server uses:
- **ASP.NET Core** for hosting and HTTP handling
- **JWT Bearer Authentication** for Azure B2C token validation
- **MCP Authentication Extensions** for OAuth resource metadata
- **HttpClient** for calling the weather.gov API
- **Authorization** to protect MCP endpoints

## Configuration Details

- **Server URL**: `http://localhost:7071`
- **Azure B2C Authority**: `https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1A_SIGNUP_SIGNIN/v2.0`
- **OAuth Metadata**: `https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1A_SIGNUP_SIGNIN/v2.0/.well-known/openid-configuration`
- **Token Endpoint**: `https://yourtenant.b2clogin.com/yourtenant.onmicrosoft.com/B2C_1A_SIGNUP_SIGNIN/oauth2/v2.0/token`
- **Token Validation**: Audience (ClientId) and issuer validation against Azure B2C
- **CORS**: Enabled for all origins (configure appropriately for production)
- **Scope Format**: `https://yourtenant.onmicrosoft.com/your-client-id/.default`

**Azure B2C vs Azure AD Differences:**
- **Azure B2C**: Requires policy in URL even for client credentials flow
- **Azure AD**: Supports policy-free client credentials flow
- **Azure B2C**: Designed for consumer identity scenarios  
- **Azure AD**: Designed for enterprise/application scenarios

## Security Considerations

- Use HTTPS in production
- Implement proper CORS policies
- Use Azure Key Vault for secrets
- Enable logging and monitoring
- Implement rate limiting
- Use least privilege access

## Troubleshooting

### Common Issues

1. **Token validation fails**: 
   - Check that the Authority URL is correct and matches your B2C tenant
   - Verify the metadata endpoint is accessible (no policy needed for client credentials)
   - Ensure the tenant domain is correct

2. **Audience validation fails**: 
   - Ensure ClientId in configuration matches the token audience
   - Check that the client ID is correct in both the token request and server configuration

3. **Issuer validation fails**: 
   - Verify the Azure B2C tenant configuration
   - Check that the issuer URL format matches the expected pattern (without policy)

4. **Authentication fails**: 
   - Verify client secret is correct and not expired
   - Check that the scope format is correct: `https://yourtenant.onmicrosoft.com/your-client-id/.default`
   - Ensure the token endpoint URL is correct (without policy path)

5. **CORS issues**: 
   - Check browser developer tools for CORS-related errors
   - Verify CORS policy configuration in the server

6. **PowerShell Unicode errors**: 
   - Use the provided test script which handles Unicode properly
   - Consider using UTF-8 encoding for HTTP files

### Debug Logging

Enable debug logging in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.Authentication": "Debug"
    }
  }
}
```

## External Dependencies

- **National Weather Service API**: The weather tools use the free API at `api.weather.gov` to fetch real weather data
- **Microsoft.AspNetCore.Authentication.JwtBearer**: For JWT token validation
- **Microsoft.IdentityModel.Tokens**: For token validation parameters
- **ModelContextProtocol.AspNetCore**: For MCP server functionality and OAuth metadata

## Key Files

- `Program.cs`: Server setup with Azure B2C authentication and MCP configuration
- `Tools/WeatherTools.cs`: Weather tool implementations
- `Tools/HttpClientExt.cs`: HTTP client extensions
- `test-azure-b2c.http`: REST client test file
- `appsettings.Development.json`: Development configuration

## Next Steps

1. Set up Azure B2C tenant and application
2. Configure the application settings
3. Test the authentication flow
4. Implement additional MCP tools as needed
5. Deploy to Azure with proper security configurations