# Dynamic Tool Filtering MCP Server Sample

This comprehensive sample demonstrates advanced tool filtering and authorization capabilities in the MCP (Model Context Protocol) C# SDK. It showcases how to implement sophisticated access control systems with multiple filter types, authentication schemes, and business logic constraints.

## Overview

The Dynamic Tool Filtering sample illustrates real-world scenarios where different users need different levels of access to tools based on roles, time constraints, quotas, feature flags, and business rules. It's designed to be educational while demonstrating production-ready patterns.

## Features

### 🔐 Multiple Filter Types

- **Role-Based Filtering**: Hierarchical role system (guest → user → premium → admin → super_admin)
- **Time-Based Filtering**: Business hours restrictions and maintenance windows
- **Scope-Based Filtering**: OAuth2-style scope checking for fine-grained permissions
- **Rate Limiting**: Per-user and per-tool rate limits with sliding/fixed windows
- **Tenant Isolation**: Multi-tenant tool access with tenant-specific configurations
- **Business Logic Filtering**: Feature flags, quota management, and environment restrictions

### 🛠️ Tool Categories

The sample includes four categories of tools representing different security levels:

1. **Public Tools** (`PublicTools.cs`): Available to all users without authentication
2. **User Tools** (`UserTools.cs`): Require basic authentication and user role
3. **Admin Tools** (`AdminTools.cs`): Require administrative privileges
4. **Premium Tools** (`PremiumTools.cs`): Advanced functionality requiring premium access

### 🔑 Authentication Methods

- **JWT Bearer Tokens**: Standard OAuth2/OIDC authentication with claims
- **API Key Authentication**: Simple header or query-based authentication
- **Role-based Claims**: Hierarchical role system with inheritance
- **Scope Claims**: Granular permission scopes for different operations

## Architecture

### Filter Priority System

Filters execute in priority order (lowest number = highest priority):

1. **Rate Limiting** (Priority 50): Enforces usage quotas first
2. **Tenant Isolation** (Priority 75): Multi-tenant access control
3. **Role-Based** (Priority 100): User role verification
4. **Scope-Based** (Priority 150): OAuth2 scope checking
5. **Time-Based** (Priority 200): Business hours and maintenance
6. **Business Logic** (Priority 300): Feature flags and environment rules

### Filter Flow Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   MCP Client    │────│ Authentication  │────│ Authorization   │
│                 │    │   & Identity    │    │    Filters      │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                                        │
                                                        ▼
┌───────────────────────────────────────────────────────────────────┐
│                    FILTER CHAIN EXECUTION                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 1. Rate Limiting Filter (Priority 50)                      │ │
│  │    ├─ Check per-user rate limits                           │ │
│  │    ├─ Validate time windows                                │ │
│  │    └─ Record usage statistics                              │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  │                                                               │
│  ▼                                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 2. Tenant Isolation Filter (Priority 75)                   │ │
│  │    ├─ Validate tenant status                               │ │
│  │    ├─ Check tenant tool allowlist                          │ │
│  │    └─ Apply tenant-specific rate limits                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  │                                                               │
│  ▼                                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 3. Role-Based Filter (Priority 100)                        │ │
│  │    ├─ Extract user roles from claims                       │ │
│  │    ├─ Check hierarchical permissions                       │ │
│  │    └─ Validate tool access patterns                        │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  │                                                               │
│  ▼                                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 4. Scope-Based Filter (Priority 150)                       │ │
│  │    ├─ Parse OAuth2 scopes                                  │ │
│  │    ├─ Match required tool scopes                           │ │
│  │    └─ Validate scope hierarchy                             │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  │                                                               │
│  ▼                                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 5. Time-Based Filter (Priority 200)                        │ │
│  │    ├─ Check business hours                                 │ │
│  │    ├─ Validate maintenance windows                         │ │
│  │    └─ Apply timezone calculations                          │ │
│  └─────────────────────────────────────────────────────────────┘ │
│  │                                                               │
│  ▼                                                               │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ 6. Business Logic Filter (Priority 300)                    │ │
│  │    ├─ Check feature flags                                  │ │
│  │    ├─ Validate quotas                                      │ │
│  │    └─ Apply environment restrictions                       │ │
│  └─────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
                        ┌─────────────────┐
                        │  Tool Execution │
                        │   or Rejection  │
                        └─────────────────┘
```

### Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        MCP SERVER                                   │
│                                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐     │
│  │  Public Tools   │  │   User Tools    │  │  Premium Tools  │     │
│  │                 │  │                 │  │                 │     │
│  │ • echo          │  │ • get_profile   │  │ • secure_random │     │
│  │ • system_info   │  │ • hash_calc     │  │ • text_analysis │     │
│  │ • utc_time      │  │ • uuid_gen      │  │ • password_gen  │     │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘     │
│                                                                     │
│                              │                                      │
│                              ▼                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │            TOOL AUTHORIZATION SERVICE                       │   │
│  │                                                             │   │
│  │  • Filter registration & management                        │   │
│  │  • Priority-based execution                                │   │
│  │  • Result aggregation & challenge generation              │   │
│  │  • Context enrichment                                     │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│                              │                                      │
│                              ▼                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                   EXTERNAL SERVICES                         │   │
│  │                                                             │   │
│  │  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐   │   │
│  │  │ Rate Limiting │  │ Feature Flags │  │ Quota Service │   │   │
│  │  │   Service     │  │   Service     │  │               │   │   │
│  │  │               │  │               │  │               │   │   │
│  │  │ • Usage cache │  │ • Flag state  │  │ • Usage track │   │   │
│  │  │ • Time windows│  │ • A/B testing │  │ • Limits mgmt │   │   │
│  │  └───────────────┘  └───────────────┘  └───────────────┘   │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

### Project Structure

```
DynamicToolFiltering/
├── Authorization/
│   └── Filters/              # Filter implementations
│       ├── BusinessLogicFilter.cs
│       ├── RateLimitingToolFilter.cs
│       ├── RoleBasedToolFilter.cs
│       ├── ScopeBasedToolFilter.cs
│       ├── TenantIsolationFilter.cs
│       └── TimeBasedToolFilter.cs
├── Configuration/            # Configuration models
│   └── FilteringOptions.cs
├── Models/                  # Data models
│   ├── FilterResult.cs
│   ├── ToolExecutionContext.cs
│   ├── UsageStatistics.cs
│   └── UserInfo.cs
├── Services/               # Supporting services
│   ├── IFeatureFlagService.cs
│   ├── IQuotaService.cs
│   ├── IRateLimitingService.cs
│   ├── InMemoryFeatureFlagService.cs
│   ├── InMemoryQuotaService.cs
│   └── InMemoryRateLimitingService.cs
├── Tools/                 # Tool implementations
│   ├── AdminTools.cs
│   ├── PremiumTools.cs
│   ├── PublicTools.cs
│   └── UserTools.cs
├── Properties/           # Launch profiles
│   └── launchSettings.json
├── docs/                # Enhanced documentation
│   ├── ARCHITECTURE.md
│   ├── DEPLOYMENT.md
│   ├── PERFORMANCE.md
│   └── TROUBLESHOOTING.md
├── scripts/             # Automation scripts
│   ├── test-all.sh
│   ├── test-all.ps1
│   └── setup-dev.sh
├── .vscode/            # VS Code configuration
│   ├── launch.json
│   ├── settings.json
│   └── tasks.json
├── appsettings.*.json  # Configuration files
├── Dockerfile          # Docker configuration
├── docker-compose.yml  # Multi-service setup
├── Program.cs
├── README.md
├── TESTING_GUIDE.md
└── INTEGRATION_EXAMPLES.md
```

## Quick Start

### 1. Prerequisites

- .NET 9.0 SDK or later
- Git (for cloning the repository)
- curl or Postman (for testing)
- Optional: Docker Desktop (for containerized deployment)
- Optional: Visual Studio Code with C# extension

### 2. One-Line Setup

```bash
# Clone, build, and run in development mode
git clone https://github.com/microsoft/mcp-csharp-sdk.git && \
cd mcp-csharp-sdk/samples/DynamicToolFiltering && \
dotnet run --launch-profile DevelopmentMode
```

### 3. Alternative Setup Methods

#### Option A: Manual Setup

```bash
# Navigate to the sample directory
cd samples/DynamicToolFiltering

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run with development profile
dotnet run --launch-profile DevelopmentMode
```

#### Option B: Docker Setup

```bash
# Build Docker image
docker build -t dynamic-tool-filtering .

# Run container
docker run -p 8080:8080 dynamic-tool-filtering
```

#### Option C: Development Environment Setup

```bash
# Run the setup script (creates .vscode config, installs tools)
./scripts/setup-dev.sh

# Open in VS Code with debugging ready
code .
```

### 4. Verify Installation

```bash
# Check server health
curl http://localhost:8080/health

# Expected response:
# {
#   "Status": "healthy",
#   "Timestamp": "2024-01-01T12:00:00.000Z",
#   "Environment": "Development",
#   "Version": "1.0.0"
# }
```

### 5. Quick API Test Suite

The sample includes predefined API keys for testing different user roles:

| Role | API Key | Available Tools | Rate Limit |
|------|---------|-----------------|------------|
| Guest | `demo-guest-key` | Public tools only | 20/hour |
| User | `demo-user-key` | Public + User tools | 100/hour |
| Premium | `demo-premium-key` | Public + User + Premium tools | 500/hour |
| Admin | `demo-admin-key` | All tools | 1000/hour |

#### Test Tool Visibility by Role

```bash
# Guest user - should see only basic tools
curl -H "X-API-Key: demo-guest-key" \
     http://localhost:8080/mcp/v1/tools

# User role - should see user-level tools
curl -H "X-API-Key: demo-user-key" \
     http://localhost:8080/mcp/v1/tools

# Premium user - should see premium tools
curl -H "X-API-Key: demo-premium-key" \
     http://localhost:8080/mcp/v1/tools

# Admin user - should see all tools
curl -H "X-API-Key: demo-admin-key" \
     http://localhost:8080/mcp/v1/tools
```

#### Test Tool Execution

```bash
# Test 1: Public tool (no authentication required)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "echo",
    "arguments": {
      "message": "Hello Dynamic Filtering!"
    }
  }'

# Test 2: User tool (requires authentication)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "get_user_profile",
    "arguments": {}
  }'

# Test 3: Premium tool (requires premium role)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-premium-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_generate_secure_random",
    "arguments": {
      "byteCount": 32,
      "format": "hex"
    }
  }'

# Test 4: Admin tool (requires admin role)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-admin-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_get_system_diagnostics",
    "arguments": {}
  }'

# Test 5: Authorization failure (user trying admin tool)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_get_system_diagnostics",
    "arguments": {}
  }'
# Expected: HTTP 401 with authorization error
```

### 6. Run Automated Test Suite

```bash
# Run comprehensive test suite (bash)
./scripts/test-all.sh

# Or PowerShell version
.\scripts\test-all.ps1

# Run specific test categories
./scripts/test-all.sh --category authentication
./scripts/test-all.sh --category rate-limiting
./scripts/test-all.sh --category performance
```

## Launch Profiles

The sample includes multiple launch profiles for different scenarios:

### Development Profiles

- **DevelopmentMode**: Basic filtering with relaxed rate limits
- **NoFilteringMode**: All filtering disabled for testing
- **MinimalFilteringMode**: Only role-based filtering enabled

### Feature-Specific Profiles

- **TenantDemoMode**: Demonstrates multi-tenant access control
- **RateLimitingDemoMode**: Shows rate limiting with strict limits (1-minute windows)
- **BusinessHoursDemoMode**: Time-based restrictions (9 AM - 5 PM weekdays)

### Environment Profiles

- **StagingMode**: All filters enabled with moderate settings
- **ProductionMode**: Strict security with all filters active

## Configuration

### Environment Variables

Override any configuration using environment variables:

```bash
# Enable/disable specific filters
export Filtering__RoleBased__Enabled=true
export Filtering__RateLimiting__Enabled=true
export Filtering__TimeBased__Enabled=false

# Customize rate limits
export Filtering__RateLimiting__WindowMinutes=60
export Filtering__RateLimiting__RoleLimits__user=100

# Business hours (UTC times)
export Filtering__TimeBased__BusinessHours__StartTime="09:00"
export Filtering__TimeBased__BusinessHours__EndTime="17:00"
```

### JWT Configuration

For JWT authentication, configure the following:

```json
{
  "Jwt": {
    "SecretKey": "your-256-bit-secret-key",
    "Issuer": "your-issuer",
    "Audience": "your-audience",
    "ExpirationMinutes": 60
  }
}
```

## Filter Implementations

### Role-Based Filter

Implements hierarchical role checking with pattern matching:

- Supports glob patterns for tool names (`admin_*`, `premium_*`)
- Hierarchical inheritance (admin inherits user permissions)
- Configurable role mappings

### Rate Limiting Filter

Implements quota management:

- Per-user and per-tool rate limits
- Sliding or fixed time windows
- Role-based default limits with tool-specific overrides
- Automatic cleanup of old usage records

### Scope-Based Filter

OAuth2-style scope checking:

- Space-separated scopes in JWT claims
- Hierarchical scope inheritance
- Wildcard scope matching
- Proper OAuth2 error responses

### Time-Based Filter

Business hours and maintenance windows:

- Configurable business hours per timezone
- Maintenance window blocking
- Tool-specific time restrictions

### Tenant Isolation Filter

Multi-tenant access control:

- Tenant-specific tool allowlists/denylists
- Custom rate limits per tenant
- Tenant activation status checking

### Business Logic Filter

Advanced business rules:

- Feature flag integration
- Quota management with periodic resets
- Environment-specific restrictions (dev/staging/prod)

## Error Handling

The sample demonstrates proper HTTP error responses with WWW-Authenticate headers:

### 401 Unauthorized Responses

```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="mcp-api", scope="admin:tools", error="insufficient_scope"
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

### Custom Challenge Headers

Different filter types generate appropriate challenge headers:

- **Bearer**: OAuth2 Bearer token challenges with scope information
- **Basic**: Basic authentication challenges
- **ApiKey**: Custom API key challenges
- **Role**: Role-based access challenges
- **Tenant**: Tenant-specific challenges

## Production Considerations

### Security

- Use secure JWT secret keys (256-bit minimum)
- Implement proper token storage and rotation
- Use HTTPS in production
- Consider rate limiting at the infrastructure level
- Implement proper audit logging

### Performance

- Use distributed caches (Redis) for rate limiting in production
- Implement proper database storage for quotas and usage tracking
- Consider caching authorization decisions
- Monitor filter performance and adjust priorities

### Scalability

- Use external feature flag services (LaunchDarkly, Azure App Configuration)
- Implement distributed quota management
- Consider eventual consistency for rate limiting
- Use proper database indexing for usage queries

## Advanced Usage

### Custom Filter Implementation

Create custom filters by implementing `IToolFilter`:

```csharp
public class CustomBusinessFilter : IToolFilter
{
    public int Priority => 250; // Between time-based and business logic

    public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        // Custom visibility logic
        return true;
    }

    public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken)
    {
        // Custom authorization logic
        return AuthorizationResult.Allow("Custom filter passed");
    }
}
```

### Dynamic Filter Registration

Filters can be registered and unregistered at runtime:

```csharp
var authService = serviceProvider.GetRequiredService<IToolAuthorizationService>();
var customFilter = new CustomBusinessFilter();
authService.RegisterFilter(customFilter);
```

### Integration with External Services

The sample demonstrates integration patterns for:

- Feature flag services
- Rate limiting backends
- Quota management systems
- Tenant management APIs

## Testing

The sample includes comprehensive testing scenarios:

1. **Authentication Testing**: Test different API keys and JWT tokens
2. **Authorization Testing**: Verify role and scope-based access
3. **Rate Limiting Testing**: Exceed limits and verify blocking
4. **Time-Based Testing**: Test business hours restrictions
5. **Feature Flag Testing**: Toggle features and verify access
6. **Error Handling Testing**: Verify proper error responses

## Troubleshooting

### Common Issues

1. **Tools not visible**: Check filter configurations and user roles
2. **Rate limit errors**: Verify rate limiting settings and time windows
3. **Authentication failures**: Check API keys and JWT configuration
4. **Time-based restrictions**: Verify timezone settings and business hours

### Debugging

Enable debug logging to see filter execution:

```json
{
  "Logging": {
    "LogLevel": {
      "DynamicToolFiltering": "Debug"
    }
  }
}
```

### Health Checks

Use the health endpoint to verify service status:

```bash
curl http://localhost:8080/health
```

## Learning Objectives

This sample demonstrates:

1. **Filter Architecture**: How to design and implement filter chains
2. **Authorization Patterns**: Multiple authentication and authorization strategies
3. **Configuration Management**: Environment-specific configurations
4. **Error Handling**: Proper HTTP error responses and challenges
5. **Performance Considerations**: Efficient filter design and caching
6. **Security Best Practices**: Secure authentication and authorization
7. **Testing Strategies**: Comprehensive testing of authorization systems

## Next Steps

- Implement persistent storage for rate limiting and quotas
- Add integration with external identity providers
- Implement audit logging for all authorization decisions
- Add metrics and monitoring for filter performance
- Create admin APIs for dynamic filter management
- Implement filter testing framework

## Contributing

This sample is designed to be educational and demonstrative. Feel free to:

- Extend with additional filter types
- Add integration with real external services
- Improve error handling and edge cases
- Add more comprehensive testing
- Enhance documentation and examples

## License

This sample is part of the MCP C# SDK and follows the same license terms.