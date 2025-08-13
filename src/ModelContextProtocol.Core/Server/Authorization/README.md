# MCP Authorization System

This directory contains the authorization system for the Model Context Protocol (MCP) C# SDK. The authorization system provides fine-grained access control for MCP tools with proper HTTP challenge responses.

## Key Components

### Core Interfaces and Classes

- **`IToolAuthorizationService`** - Main service for orchestrating tool authorization
- **`IToolFilter`** - Interface for implementing custom authorization filters
- **`AuthorizationResult`** - Represents the result of an authorization check
- **`AuthorizationChallenge`** - Represents HTTP authorization challenges (WWW-Authenticate headers)
- **`AuthorizationHttpException`** - Exception for authorization failures that require HTTP challenges
- **`ToolAuthorizationContext`** - Context information for authorization decisions

### Built-in Filters

- **`AllowAllToolFilter`** - Allows access to all tools (default behavior)
- **`DenyAllToolFilter`** - Denies access to all tools
- **`ToolNamePatternFilter`** - Filters tools based on name patterns
- **`RoleBasedToolFilterBuilder`** - Builder for role-based authorization

## Usage Examples

### Basic Setup

```csharp
// Configure the authorization service
services.AddSingleton<IToolAuthorizationService>(sp =>
{
    var authService = new ToolAuthorizationService();
    
    // Add your custom filters
    authService.RegisterFilter(new MyCustomToolFilter());
    
    return authService;
});

// Register your MCP server with tools
services.AddMcpServer(options =>
{
    options.WithTools<MyToolsClass>();
});
```

### OAuth2 Bearer Token Authorization

```csharp
public class OAuth2ToolFilter : IToolFilter
{
    public int Priority => 100;

    public Task<AuthorizationResult> AuthorizeAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        // Extract and validate Bearer token
        var token = ExtractBearerToken(context);
        
        if (string.IsNullOrEmpty(token))
        {
            // No token provided - request authentication
            return Task.FromResult(AuthorizationResult.DenyInvalidToken("my-api"));
        }

        var claims = ValidateToken(token);
        if (claims == null)
        {
            // Invalid token - request new authentication
            return Task.FromResult(AuthorizationResult.DenyInvalidToken("my-api", "Token is expired or invalid"));
        }

        // Check if tool requires specific scope
        var requiredScope = GetRequiredScope(tool.Name);
        if (requiredScope != null && !HasScope(claims, requiredScope))
        {
            // Insufficient scope - request higher privileges
            return Task.FromResult(AuthorizationResult.DenyInsufficientScope(requiredScope, "my-api"));
        }

        return Task.FromResult(AuthorizationResult.Allow("Valid token with sufficient scope"));
    }

    private string? ExtractBearerToken(ToolAuthorizationContext context)
    {
        // Extract token from context (implementation depends on your setup)
        // You might get this from HTTP headers, session data, etc.
        return null;
    }

    private ClaimsPrincipal? ValidateToken(string token)
    {
        // Validate JWT token and return claims
        // Implementation depends on your OAuth2 provider
        return null;
    }

    private string? GetRequiredScope(string toolName)
    {
        // Return the required scope for the tool
        return toolName.Contains("admin") ? "admin:tools" : "user:tools";
    }

    private bool HasScope(ClaimsPrincipal claims, string scope)
    {
        // Check if the claims contain the required scope
        return claims.HasClaim("scope", scope);
    }
}
```

### Role-Based Authorization

```csharp
public class RoleBasedToolFilter : IToolFilter
{
    public int Priority => 50;

    public Task<AuthorizationResult> AuthorizeAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        var userRole = GetUserRole(context);
        var requiredRole = GetRequiredRole(tool.Name);

        if (!HasRole(userRole, requiredRole))
        {
            // Create a custom challenge for role-based access
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "Role", 
                ("required_role", requiredRole),
                ("user_role", userRole ?? "none"));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(
                $"Tool '{tool.Name}' requires '{requiredRole}' role, but user has '{userRole}' role",
                challenge));
        }

        return Task.FromResult(AuthorizationResult.Allow($"User has required role: {requiredRole}"));
    }

    private string? GetUserRole(ToolAuthorizationContext context)
    {
        // Extract user role from context
        return "user"; // Example
    }

    private string GetRequiredRole(string toolName)
    {
        // Determine required role based on tool name
        return toolName.StartsWith("admin_") ? "admin" : "user";
    }

    private bool HasRole(string? userRole, string requiredRole)
    {
        // Check if user has the required role
        return userRole == requiredRole || (userRole == "admin" && requiredRole == "user");
    }
}
```

### API Key Authorization

```csharp
public class ApiKeyToolFilter : IToolFilter
{
    private readonly Dictionary<string, string[]> _apiKeyScopes;

    public ApiKeyToolFilter(Dictionary<string, string[]> apiKeyScopes)
    {
        _apiKeyScopes = apiKeyScopes;
    }

    public int Priority => 75;

    public Task<AuthorizationResult> AuthorizeAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
    {
        var apiKey = ExtractApiKey(context);
        
        if (string.IsNullOrEmpty(apiKey))
        {
            // No API key provided
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "ApiKey",
                ("realm", "mcp-api"),
                ("parameter", "X-API-Key"));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(
                "API key required", challenge));
        }

        if (!_apiKeyScopes.TryGetValue(apiKey, out var scopes))
        {
            // Invalid API key
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "ApiKey",
                ("realm", "mcp-api"),
                ("error", "invalid_key"));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(
                "Invalid API key", challenge));
        }

        var requiredScope = GetRequiredScope(tool.Name);
        if (requiredScope != null && !scopes.Contains(requiredScope))
        {
            // Insufficient scope for API key
            var challenge = AuthorizationChallenge.CreateCustomChallenge(
                "ApiKey",
                ("realm", "mcp-api"),
                ("required_scope", requiredScope),
                ("available_scopes", string.Join(",", scopes)));

            return Task.FromResult(AuthorizationResult.DenyWithChallenge(
                $"API key does not have required scope: {requiredScope}", challenge));
        }

        return Task.FromResult(AuthorizationResult.Allow("Valid API key with sufficient scope"));
    }

    private string? ExtractApiKey(ToolAuthorizationContext context)
    {
        // Extract API key from context (e.g., from HTTP headers)
        return null;
    }

    private string? GetRequiredScope(string toolName)
    {
        // Determine required scope for the tool
        return toolName.Contains("write") ? "write" : "read";
    }
}
```

## HTTP Challenge Responses

When authorization fails, the system automatically generates proper HTTP responses with WWW-Authenticate headers:

### OAuth2 Bearer Token Challenge
```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="mcp-api", scope="write:tools", error="insufficient_scope", error_description="The request requires higher privileges"
Content-Type: application/json

{
  "error": {
    "code": -32002,
    "message": "Access denied for tool 'admin_tool': Insufficient scope",
    "data": {
      "ToolName": "admin_tool",
      "Reason": "Insufficient scope",
      "HttpStatusCode": 401,
      "RequiresAuthentication": true
    }
  }
}
```

### Basic Authentication Challenge
```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Basic realm="mcp-api"
Content-Type: application/json

{
  "error": {
    "code": -32002,
    "message": "Access denied for tool 'secure_tool': Authentication required",
    "data": {
      "ToolName": "secure_tool",
      "Reason": "Authentication required",
      "HttpStatusCode": 401,
      "RequiresAuthentication": true
    }
  }
}
```

### Custom Authentication Challenge
```
HTTP/1.1 401 Unauthorized
WWW-Authenticate: ApiKey realm="mcp-api", parameter="X-API-Key"
Content-Type: application/json

{
  "error": {
    "code": -32002,
    "message": "Access denied for tool 'api_tool': API key required",
    "data": {
      "ToolName": "api_tool",
      "Reason": "API key required",
      "HttpStatusCode": 401,
      "RequiresAuthentication": true
    }
  }
}
```

## Filter Priority and Execution Order

Filters are executed in priority order (highest to lowest). If any filter denies access, the authorization fails immediately. All filters must allow access for the tool to be authorized.

```csharp
// Higher priority filters run first
authService.RegisterFilter(new SecurityFilter { Priority = 1000 });
authService.RegisterFilter(new RoleFilter { Priority = 500 });
authService.RegisterFilter(new ScopeFilter { Priority = 100 });
```

## Best Practices

1. **Use specific error messages** - Provide clear reasons for authorization failures
2. **Include proper challenges** - Always provide WWW-Authenticate headers for 401 responses
3. **Implement proper token validation** - Validate tokens securely and check expiration
4. **Use appropriate HTTP status codes** - 401 for authentication issues, 403 for authorization issues
5. **Log authorization events** - Track authorization successes and failures for security monitoring
6. **Cache authorization decisions** - Consider caching where appropriate to improve performance
7. **Handle errors gracefully** - Fail securely when authorization checks encounter errors

## Security Considerations

- Never expose sensitive information in error messages
- Use HTTPS in production to protect credentials
- Implement proper token storage and handling
- Consider rate limiting for authorization endpoints
- Regularly audit and rotate API keys and secrets
- Implement proper logging for security events