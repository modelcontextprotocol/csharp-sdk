# Troubleshooting Guide

This guide helps you diagnose and resolve common issues with the Dynamic Tool Filtering MCP server.

## Table of Contents

1. [Quick Diagnostics](#quick-diagnostics)
2. [Common Issues](#common-issues)
3. [Authentication Problems](#authentication-problems)
4. [Authorization Failures](#authorization-failures)
5. [Performance Issues](#performance-issues)
6. [Configuration Problems](#configuration-problems)
7. [Docker Issues](#docker-issues)
8. [Development Environment Issues](#development-environment-issues)
9. [Logging and Debugging](#logging-and-debugging)
10. [Getting Help](#getting-help)

## Quick Diagnostics

### Health Check Commands

```bash
# Basic health check
curl http://localhost:8080/health

# Expected response:
# {
#   "Status": "healthy",
#   "Timestamp": "2024-01-01T12:00:00.000Z",
#   "Environment": "Development",
#   "Version": "1.0.0"
# }

# Detailed server information
curl -v http://localhost:8080/health

# Check if server is listening
netstat -tlnp | grep :8080
# or
ss -tlnp | grep :8080
```

### Quick Test Script

```bash
#!/bin/bash
# scripts/quick-diagnose.sh

BASE_URL="http://localhost:8080"

echo "=== Quick Diagnostics ==="

# Test 1: Server responding
echo -n "Server health: "
if curl -s -f "$BASE_URL/health" > /dev/null; then
    echo "✅ OK"
else
    echo "❌ FAILED - Server not responding"
    exit 1
fi

# Test 2: Authentication working
echo -n "Authentication: "
response=$(curl -s -w "%{http_code}" -H "X-API-Key: demo-user-key" "$BASE_URL/mcp/v1/tools")
if [[ "${response: -3}" == "200" ]]; then
    echo "✅ OK"
else
    echo "❌ FAILED - HTTP ${response: -3}"
fi

# Test 3: Tool execution
echo -n "Tool execution: "
response=$(curl -s -w "%{http_code}" -X POST \
    -H "Content-Type: application/json" \
    -d '{"name": "echo", "arguments": {"message": "test"}}' \
    "$BASE_URL/mcp/v1/tools/call")
if [[ "${response: -3}" == "200" ]]; then
    echo "✅ OK"
else
    echo "❌ FAILED - HTTP ${response: -3}"
fi

echo "Diagnostics complete"
```

## Common Issues

### 1. Server Won't Start

**Symptoms:**
- Application exits immediately
- Port binding errors
- Missing dependencies

**Solutions:**

```bash
# Check if port is already in use
lsof -i :8080
# Kill existing process if needed
kill -9 <PID>

# Verify .NET installation
dotnet --version
# Should show 9.0.x or later

# Check project dependencies
dotnet restore
dotnet build

# Run with verbose logging
DOTNET_ENVIRONMENT=Development dotnet run --verbosity diagnostic
```

**Common Error Messages:**

```
Error: Unable to bind to https://localhost:8080
Solution: Change port in launchSettings.json or kill process using port 8080

Error: Could not load file or assembly
Solution: Run 'dotnet restore' and 'dotnet build'

Error: The configured user limit (128) on the number of inotify instances has been reached
Solution: Increase inotify limit: echo fs.inotify.max_user_instances=524288 | sudo tee -a /etc/sysctl.conf
```

### 2. Tools Not Visible

**Symptoms:**
- Empty tool list
- Some tools missing for specific roles
- All tools showing regardless of role

**Diagnosis:**

```bash
# Check tool visibility for different roles
echo "=== Tool Visibility Test ==="

for role in "demo-guest-key" "demo-user-key" "demo-premium-key" "demo-admin-key"; do
    echo "Role: $role"
    curl -s -H "X-API-Key: $role" http://localhost:8080/mcp/v1/tools | \
        jq -r '.result.tools[].name' | sort
    echo ""
done
```

**Solutions:**

```bash
# Check filter configuration
grep -r "Filtering" appsettings*.json

# Verify filters are registered
grep -A 20 "ConfigureFiltering" Program.cs

# Check logs for filter execution
tail -f logs/dynamic-tool-filtering-*.log | grep -i filter
```

### 3. All Tools Visible Regardless of Role

**Cause:** Filtering disabled or role-based filter not working

**Solutions:**

```bash
# Check if filtering is enabled
curl -s http://localhost:8080/health | jq .

# Verify environment variables
printenv | grep Filtering

# Check role extraction in logs
# Look for: "User roles extracted: [role_name]"
```

## Authentication Problems

### 1. Invalid API Key Errors

**Symptoms:**
```json
{
  "error": {
    "code": -32002,
    "message": "Invalid API key"
  }
}
```

**Solutions:**

```bash
# Verify API key format
echo "Your API key should be one of:"
echo "- demo-guest-key (guest role)"
echo "- demo-user-key (user role)"
echo "- demo-premium-key (premium role)"
echo "- demo-admin-key (admin role)"

# Test with correct key
curl -H "X-API-Key: demo-user-key" http://localhost:8080/mcp/v1/tools

# Check key extraction in code
grep -A 10 "HandleAuthenticateAsync" Program.cs
```

### 2. JWT Token Issues

**Symptoms:**
```json
{
  "error": {
    "code": -32002,
    "message": "JWT token validation failed"
  }
}
```

**Solutions:**

```bash
# Verify JWT configuration
grep -A 10 "JwtBearer" Program.cs

# Check token format (should be: Authorization: Bearer <token>)
# Test JWT token generation
# Use online JWT debugger: jwt.io

# Verify secret key matches
grep "SecretKey" appsettings*.json
```

## Authorization Failures

### 1. User Trying to Access Admin Tools

**Expected Behavior:**
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

**If this doesn't happen:**

```bash
# Check role-based filter is enabled
grep "RoleBased.*Enabled" appsettings*.json

# Verify filter priority
grep -A 5 "RoleBasedToolFilter" Program.cs

# Check role extraction logic
tail -f logs/dynamic-tool-filtering-*.log | grep -i "role"
```

### 2. Rate Limiting Not Working

**Test Rate Limiting:**

```bash
# Rapid requests test
for i in {1..25}; do
    echo "Request $i:"
    curl -s -w "HTTP %{http_code}\n" -o /dev/null \
        -H "X-API-Key: demo-guest-key" \
        -X POST \
        -H "Content-Type: application/json" \
        -d '{"name": "echo", "arguments": {"message": "test"}}' \
        http://localhost:8080/mcp/v1/tools/call
    sleep 0.1
done
```

**Expected:** Some requests return HTTP 429 after hitting limit

**If rate limiting not working:**

```bash
# Check rate limiting configuration
grep -A 10 "RateLimiting" appsettings*.json

# Verify rate limiting service registration
grep "IRateLimitingService" Program.cs

# Check rate limiting logs
tail -f logs/dynamic-tool-filtering-*.log | grep -i "rate"
```

## Performance Issues

### 1. Slow Response Times

**Diagnosis:**

```bash
# Measure response times
time curl -s http://localhost:8080/mcp/v1/tools > /dev/null

# Load test
ab -n 100 -c 5 http://localhost:8080/health

# Check for memory leaks
# Monitor memory usage over time
while true; do
    ps aux | grep DynamicToolFiltering | grep -v grep | awk '{print $4 "%", $6/1024 "MB"}'
    sleep 5
done
```

**Solutions:**

```bash
# Enable performance logging
export DOTNET_EnableEventLog=true

# Use dotnet-trace for profiling
dotnet-trace collect --process-id $(pgrep -f DynamicToolFiltering)

# Check for inefficient filters
# Look for filters taking > 100ms
tail -f logs/dynamic-tool-filtering-*.log | grep -E "(Filter.*took|duration.*ms)"
```

### 2. High Memory Usage

**Diagnosis:**

```bash
# Monitor memory
dotnet-counters monitor --process-id $(pgrep -f DynamicToolFiltering) --counters System.Runtime

# Check for leaks
dotnet-gcdump collect --process-id $(pgrep -f DynamicToolFiltering)
```

**Solutions:**

```bash
# Review caching configuration
grep -r "MemoryCache" . --include="*.cs"

# Check for proper disposal
grep -r "using\|Dispose" . --include="*.cs"

# Optimize garbage collection
export DOTNET_gcServer=1
export DOTNET_gcConcurrent=1
```

## Configuration Problems

### 1. Environment Variables Not Working

**Diagnosis:**

```bash
# List all environment variables
printenv | grep -i filtering

# Check configuration binding
grep -A 20 "Configure<FilteringOptions>" Program.cs

# Test configuration reading
dotnet run -- --help
```

**Solutions:**

```bash
# Set environment variables properly
export Filtering__Enabled=true
export Filtering__RoleBased__Enabled=true

# Use appsettings file instead
# Edit appsettings.Development.json
```

### 2. Launch Profile Issues

**Problem:** Launch profile not found or not working

**Solutions:**

```bash
# List available profiles
grep -A 5 "profiles" Properties/launchSettings.json

# Run specific profile
dotnet run --launch-profile DevelopmentMode

# Use environment variables directly
ASPNETCORE_ENVIRONMENT=Development dotnet run
```

## Docker Issues

### 1. Container Won't Start

**Diagnosis:**

```bash
# Check container logs
docker logs dynamic-tool-filtering

# Inspect container
docker inspect dynamic-tool-filtering

# Check port mapping
docker port dynamic-tool-filtering
```

**Solutions:**

```bash
# Rebuild container
docker build --no-cache -t dynamic-tool-filtering .

# Run with different port
docker run -p 9080:8080 dynamic-tool-filtering

# Check Dockerfile configuration
grep EXPOSE Dockerfile
```

### 2. Health Check Failing

**Diagnosis:**

```bash
# Check health status
docker ps --format "table {{.Names}}\t{{.Status}}"

# Test health endpoint manually
docker exec dynamic-tool-filtering curl -f http://localhost:8080/health
```

**Solutions:**

```bash
# Increase health check timeout
# Edit docker-compose.yml:
# healthcheck:
#   timeout: 30s
#   start_period: 60s

# Disable health check temporarily
docker run --no-healthcheck dynamic-tool-filtering
```

## Development Environment Issues

### 1. VS Code Debugging Not Working

**Solutions:**

```bash
# Verify C# extension installed
code --list-extensions | grep ms-dotnettools.csharp

# Install omnisharp
dotnet tool install -g omnisharp

# Clear omnisharp cache
rm -rf ~/.omnisharp

# Reload window in VS Code
# Ctrl+Shift+P -> "Developer: Reload Window"
```

### 2. IntelliSense Not Working

**Solutions:**

```bash
# Restore packages
dotnet restore

# Clean and rebuild
dotnet clean && dotnet build

# Check omnisharp logs in VS Code
# View -> Output -> OmniSharp Log
```

## Logging and Debugging

### 1. Enable Debug Logging

**appsettings.Development.json:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "DynamicToolFiltering": "Trace",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### 2. Filter-Specific Logging

```json
{
  "Logging": {
    "LogLevel": {
      "DynamicToolFiltering.Authorization.Filters": "Debug"
    }
  }
}
```

### 3. Structured Logging Queries

```bash
# Find authorization failures
grep "Access denied" logs/dynamic-tool-filtering-*.log

# Find rate limiting events
grep "Rate limit" logs/dynamic-tool-filtering-*.log

# Find slow requests
grep -E "took [0-9]{3,}" logs/dynamic-tool-filtering-*.log
```

### 4. Real-time Log Monitoring

```bash
# Monitor all logs
tail -f logs/dynamic-tool-filtering-*.log

# Monitor specific events
tail -f logs/dynamic-tool-filtering-*.log | grep -E "(ERROR|WARNING|Rate|Auth)"

# Monitor with highlighting
tail -f logs/dynamic-tool-filtering-*.log | grep --color=always -E "(ERROR|WARNING|$)"
```

## Advanced Debugging

### 1. Enable Request/Response Logging

```csharp
// Add to Program.cs
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Request: {Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
    logger.LogInformation("Response: {StatusCode}", context.Response.StatusCode);
});
```

### 2. Filter Execution Tracing

```csharp
// Add to any filter
public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken)
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var result = await DoFilterLogic(tool, context, cancellationToken);
        _logger.LogDebug("Filter {FilterName} for tool {ToolName}: {Result} (took {ElapsedMs}ms)", 
            GetType().Name, tool.Name, result, stopwatch.ElapsedMilliseconds);
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Filter {FilterName} failed for tool {ToolName}", GetType().Name, tool.Name);
        throw;
    }
}
```

### 3. Memory Dump Analysis

```bash
# Create memory dump
dotnet-dump collect --process-id $(pgrep -f DynamicToolFiltering)

# Analyze with dotnet-dump
dotnet-dump analyze core_20240101_120000

# Common commands in analyzer:
# dumpheap -stat
# gcroot <address>
# dumpheap -mt <method_table>
```

## Getting Help

### 1. Collect Diagnostic Information

Create a diagnostic script:

```bash
#!/bin/bash
# scripts/collect-diagnostics.sh

echo "=== System Information ==="
uname -a
dotnet --info

echo "=== Process Information ==="
ps aux | grep -i dotnet

echo "=== Port Usage ==="
netstat -tlnp | grep 8080

echo "=== Configuration ==="
cat appsettings*.json

echo "=== Recent Logs ==="
tail -50 logs/dynamic-tool-filtering-*.log

echo "=== Environment Variables ==="
printenv | grep -E "(DOTNET|ASPNETCORE|Filtering)"

echo "=== Docker Status ==="
docker ps | grep dynamic-tool-filtering || echo "Not running in Docker"
```

### 2. Create Issue Report Template

```markdown
## Issue Description
Brief description of the problem

## Environment
- OS: 
- .NET Version: 
- Docker: Yes/No
- Launch Profile: 

## Steps to Reproduce
1. 
2. 
3. 

## Expected Behavior
What should happen

## Actual Behavior
What actually happens

## Logs
```
[Include relevant log entries]
```

## Configuration
```json
[Include relevant configuration]
```

## Additional Context
Any other relevant information
```

### 3. Support Channels

- Check existing issues in the repository
- Review documentation and samples
- Use the diagnostic scripts provided
- Include full error messages and logs
- Provide minimal reproduction steps

## Preventive Measures

### 1. Regular Health Checks

```bash
# Add to crontab for production monitoring
*/5 * * * * curl -f http://localhost:8080/health || echo "Health check failed" | mail -s "MCP Server Alert" admin@company.com
```

### 2. Log Rotation

```bash
# Configure logrotate
echo '/app/logs/*.log {
    daily
    rotate 30
    compress
    missingok
    notifempty
    copytruncate
}' > /etc/logrotate.d/mcp-server
```

### 3. Resource Monitoring

```bash
# Monitor resource usage
watch -n 5 'ps aux | grep DynamicToolFiltering | grep -v grep'
```

This troubleshooting guide covers the most common issues you'll encounter with the Dynamic Tool Filtering MCP server. Keep it handy for quick reference during development and deployment.