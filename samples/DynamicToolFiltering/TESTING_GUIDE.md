# Testing Guide for Dynamic Tool Filtering Sample

This guide provides comprehensive testing scenarios and examples for the Dynamic Tool Filtering MCP server sample.

## Quick Start Testing

### 1. Start the Server

```bash
cd samples/DynamicToolFiltering
dotnet run --launch-profile DevelopmentMode
```

The server will start on `http://localhost:8080` with development-friendly settings.

### 2. Basic Health Check

```bash
curl http://localhost:8080/health
```

Expected response:
```json
{
  "Status": "healthy",
  "Timestamp": "2024-01-01T12:00:00.000Z",
  "Environment": "Development",
  "Version": "1.0.0"
}
```

## API Key Testing

The sample includes predefined API keys for different user roles:

| API Key | Role | Scopes | Description |
|---------|------|--------|-------------|
| `demo-guest-key` | guest | basic:tools | Limited access to public tools |
| `demo-user-key` | user | user:tools, read:tools, basic:tools | Standard user access |
| `demo-premium-key` | premium | premium:tools, user:tools, read:tools, basic:tools | Premium features |
| `demo-admin-key` | admin | admin:tools, premium:tools, user:tools, read:tools, basic:tools | Full administrative access |

## Test Scenarios

### 1. Tool Visibility Testing

Test which tools are visible to different user roles:

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

### 2. Tool Execution Testing

#### Public Tools (No Authentication Required)

```bash
# Echo tool - available to everyone
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "echo",
    "arguments": {
      "message": "Hello Dynamic Filtering!"
    }
  }'

# System info - public tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "get_system_info",
    "arguments": {}
  }'

# UTC time - public tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "get_utc_time",
    "arguments": {}
  }'
```

#### User Tools (Requires Authentication)

```bash
# User profile - requires user role or higher
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "get_user_profile",
    "arguments": {}
  }'

# Hash calculation - user tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "calculate_hash",
    "arguments": {
      "text": "Hello World",
      "algorithm": "sha256"
    }
  }'

# UUID generation - user tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "generate_uuid",
    "arguments": {
      "count": 3
    }
  }'

# Email validation - user tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "validate_email",
    "arguments": {
      "email": "test@example.com"
    }
  }'
```

#### Premium Tools (Requires Premium Role)

```bash
# Secure random generation - premium tool
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

# Text analysis - premium tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-premium-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_analyze_text",
    "arguments": {
      "text": "This is a sample text for comprehensive analysis. It contains multiple sentences and various words to demonstrate the text analysis capabilities.",
      "depth": "comprehensive"
    }
  }'

# Password generation - premium tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-premium-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_generate_password",
    "arguments": {
      "length": 16,
      "includeUppercase": true,
      "includeLowercase": true,
      "includeNumbers": true,
      "includeSpecial": true,
      "excludeAmbiguous": true
    }
  }'

# Performance benchmark - premium tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-premium-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_performance_benchmark",
    "arguments": {
      "benchmarkType": "cpu",
      "durationSeconds": 3
    }
  }'
```

#### Admin Tools (Requires Admin Role)

```bash
# System diagnostics - admin tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-admin-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_get_system_diagnostics",
    "arguments": {}
  }'

# Force garbage collection - admin tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-admin-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_force_gc",
    "arguments": {
      "generation": -1
    }
  }'

# List processes - admin tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-admin-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_list_processes",
    "arguments": {
      "limit": 10
    }
  }'

# Reload configuration - admin tool
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-admin-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_reload_config",
    "arguments": {
      "section": "filtering"
    }
  }'
```

### 3. Authorization Failure Testing

Test that users cannot access tools above their privilege level:

```bash
# Try to access admin tool with user key (should fail)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "admin_get_system_diagnostics",
    "arguments": {}
  }'

# Try to access premium tool with guest key (should fail)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-guest-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_generate_secure_random",
    "arguments": {
      "byteCount": 32
    }
  }'

# Try to access user tool without authentication (should fail)
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "get_user_profile",
    "arguments": {}
  }'
```

Expected response for unauthorized access:
```json
{
  "error": {
    "code": -32002,
    "message": "Access denied for tool 'admin_get_system_diagnostics': Tool requires role(s): admin or super_admin. User has role(s): user",
    "data": {
      "ToolName": "admin_get_system_diagnostics",
      "Reason": "Tool requires role(s): admin or super_admin. User has role(s): user",
      "HttpStatusCode": 401,
      "RequiresAuthentication": true
    }
  }
}
```

### 4. Rate Limiting Testing

Test rate limiting by making multiple rapid requests:

```bash
# Test rate limiting with guest account (limited to 20 requests in development)
for i in {1..25}; do
  echo "Request $i:"
  curl -H "X-API-Key: demo-guest-key" \
       -X POST http://localhost:8080/mcp/v1/tools/call \
       -H "Content-Type: application/json" \
       -d '{"name": "echo", "arguments": {"message": "Test '$i'"}}' \
       -w "Status: %{http_code}\n" -s -o /dev/null
  sleep 0.1
done
```

After hitting the limit, you should see HTTP 429 responses with a rate limit error.

### 5. Feature Flag Testing

Test feature flag functionality:

```bash
# Check current feature flags (admin only)
curl -H "X-API-Key: demo-admin-key" \
     http://localhost:8080/admin/feature-flags

# Toggle a feature flag (admin only)
curl -X POST \
     -H "X-API-Key: demo-admin-key" \
     "http://localhost:8080/admin/feature-flags/premium_features?enabled=false"

# Try to use a premium tool after disabling the feature flag
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-premium-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "premium_generate_secure_random",
    "arguments": {
      "byteCount": 32
    }
  }'

# Re-enable the feature flag
curl -X POST \
     -H "X-API-Key: demo-admin-key" \
     "http://localhost:8080/admin/feature-flags/premium_features?enabled=true"
```

## Launch Profile Testing

Test different launch profiles to verify filter configurations:

### 1. No Filtering Mode

```bash
dotnet run --launch-profile NoFilteringMode
```

In this mode, all tools should be accessible regardless of authentication.

### 2. Rate Limiting Demo Mode

```bash
dotnet run --launch-profile RateLimitingDemoMode
```

This mode has very strict rate limits (1-minute windows with low limits):
- Guest: 3 requests per minute
- User: 10 requests per minute
- Premium: 25 requests per minute
- Admin: 100 requests per minute

### 3. Business Hours Demo Mode

```bash
dotnet run --launch-profile BusinessHoursDemoMode
```

This mode restricts admin tools to business hours (9 AM - 5 PM weekdays UTC).

### 4. Tenant Demo Mode

```bash
dotnet run --launch-profile TenantDemoMode
```

This mode enables tenant isolation. Add `X-Tenant-ID` header to test:

```bash
curl -H "X-API-Key: demo-user-key" \
     -H "X-Tenant-ID: tenant-a" \
     -X POST http://localhost:8080/mcp/v1/tools/call \
     -H "Content-Type: application/json" \
     -d '{"name": "echo", "arguments": {"message": "Tenant A"}}'
```

## Error Response Testing

### 1. Invalid API Key

```bash
curl -H "X-API-Key: invalid-key" \
     http://localhost:8080/mcp/v1/tools
```

### 2. Malformed Request

```bash
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{"invalid": "json"}'
```

### 3. Tool Not Found

```bash
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "nonexistent_tool",
    "arguments": {}
  }'
```

### 4. Invalid Arguments

```bash
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "X-API-Key: demo-user-key" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "calculate_hash",
    "arguments": {
      "text": "test",
      "algorithm": "invalid_algorithm"
    }
  }'
```

## Performance Testing

### 1. Concurrent Request Testing

Use a tool like Apache Bench to test concurrent requests:

```bash
# Install apache2-utils if not installed
# Ubuntu/Debian: sudo apt-get install apache2-utils
# macOS: brew install httpd

# Test with 10 concurrent connections, 100 total requests
ab -n 100 -c 10 -H "X-API-Key: demo-user-key" \
   -p echo_data.json -T application/json \
   http://localhost:8080/mcp/v1/tools/call
```

Create `echo_data.json`:
```json
{
  "name": "echo",
  "arguments": {
    "message": "Performance test"
  }
}
```

### 2. Memory Usage Testing

Monitor memory usage during heavy load:

```bash
# Run server in background
dotnet run --launch-profile DevelopmentMode &
SERVER_PID=$!

# Monitor memory usage
while true; do
  ps -o pid,vsz,rss,comm -p $SERVER_PID
  sleep 5
done

# Kill server when done
kill $SERVER_PID
```

## JWT Token Testing

For JWT testing, you need to generate valid tokens. Here's a simple example using a script or online JWT generator:

### JWT Payload Example

```json
{
  "sub": "user123",
  "name": "Test User",
  "role": "premium",
  "scope": "premium:tools user:tools read:tools basic:tools",
  "iat": 1609459200,
  "exp": 1609462800,
  "iss": "dynamic-tool-filtering-demo",
  "aud": "mcp-api-clients"
}
```

Use the secret key from configuration: `your-256-bit-secret-key-here-make-it-secure-and-change-in-production`

Test with JWT:

```bash
curl -H "Authorization: Bearer YOUR_JWT_TOKEN" \
     http://localhost:8080/mcp/v1/tools
```

## Automated Testing Script

Create a comprehensive test script:

```bash
#!/bin/bash

BASE_URL="http://localhost:8080"
GUEST_KEY="demo-guest-key"
USER_KEY="demo-user-key"
PREMIUM_KEY="demo-premium-key"
ADMIN_KEY="demo-admin-key"

echo "=== Dynamic Tool Filtering Test Suite ==="

# Test 1: Health Check
echo "Test 1: Health Check"
curl -s "$BASE_URL/health" | jq .
echo ""

# Test 2: Tool Visibility by Role
echo "Test 2: Tool Visibility"
echo "Guest tools:"
curl -s -H "X-API-Key: $GUEST_KEY" "$BASE_URL/mcp/v1/tools" | jq '.result.tools[].name'
echo "User tools:"
curl -s -H "X-API-Key: $USER_KEY" "$BASE_URL/mcp/v1/tools" | jq '.result.tools[].name'
echo "Premium tools:"
curl -s -H "X-API-Key: $PREMIUM_KEY" "$BASE_URL/mcp/v1/tools" | jq '.result.tools[].name'
echo "Admin tools:"
curl -s -H "X-API-Key: $ADMIN_KEY" "$BASE_URL/mcp/v1/tools" | jq '.result.tools[].name'
echo ""

# Test 3: Successful Tool Execution
echo "Test 3: Successful Tool Execution"
curl -s -X POST "$BASE_URL/mcp/v1/tools/call" \
  -H "Content-Type: application/json" \
  -d '{"name": "echo", "arguments": {"message": "Test"}}' | jq .
echo ""

# Test 4: Authorization Failure
echo "Test 4: Authorization Failure"
curl -s -X POST "$BASE_URL/mcp/v1/tools/call" \
  -H "X-API-Key: $GUEST_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "admin_get_system_diagnostics", "arguments": {}}' | jq .
echo ""

echo "=== Test Suite Complete ==="
```

## Troubleshooting

### Common Issues

1. **Server not starting**: Check that port 8080 is available
2. **Tools not visible**: Verify API key and user role configuration
3. **Rate limit errors**: Wait for rate limit window to reset or use a different launch profile
4. **Feature flag errors**: Check feature flag configuration and current state

### Debug Logging

Enable debug logging in `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "DynamicToolFiltering": "Debug",
      "ModelContextProtocol": "Debug"
    }
  }
}
```

### Verbose Filter Logging

Check the console output for detailed filter execution logs:

```
[Debug] Tool inclusion check for echo: User roles [guest], Required roles [guest, user, premium, admin, super_admin], HasAccess: True
[Debug] Tool execution authorized: echo for user anonymous_12345678. Remaining: 19/20
```

This guide provides comprehensive testing coverage for all aspects of the Dynamic Tool Filtering sample, from basic functionality to advanced scenarios and edge cases.