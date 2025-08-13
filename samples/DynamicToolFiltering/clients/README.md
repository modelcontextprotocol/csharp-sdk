# Dynamic Tool Filtering Test Client

A comprehensive test client for the Dynamic Tool Filtering MCP server that demonstrates client-side integration patterns and provides automated testing capabilities.

## Features

- **Health Monitoring**: Check server availability and status
- **Tool Discovery**: Explore available tools based on user permissions
- **Authentication Testing**: Test various authentication methods (API key, JWT)
- **Role-based Access Control Validation**: Verify hierarchical permission enforcement
- **Performance Testing**: Load testing with concurrent users
- **Error Handling Demonstration**: Test edge cases and error scenarios
- **Interactive Demo Mode**: Guided demonstration of server capabilities

## Quick Start

### Prerequisites

- .NET 9.0 SDK
- Running Dynamic Tool Filtering MCP server

### Build and Run

```bash
# Navigate to the client directory
cd clients

# Restore dependencies
dotnet restore

# Build the client
dotnet build

# Run basic health check
dotnet run -- health

# Discover tools with user permissions
dotnet run -- discover --api-key demo-user-key

# Execute a tool
dotnet run -- execute --tool echo --args '{"message":"Hello World"}' --api-key demo-user-key

# Run comprehensive test suite
dotnet run -- test

# Run performance tests
dotnet run -- perf --users 5 --requests 20

# Interactive demonstration
dotnet run -- demo
```

## Commands

### Health Check

Check if the server is running and responding correctly:

```bash
dotnet run -- health --url http://localhost:8080
```

**Expected Output:**
```
🏥 Checking server health...
✅ Server is healthy!
   Status: healthy
   Environment: Development
   Version: 1.0.0
   Timestamp: 2024-01-01 12:00:00
```

### Tool Discovery

Discover available tools for different user roles:

```bash
# Public access (no authentication)
dotnet run -- discover

# Guest user access
dotnet run -- discover --api-key demo-guest-key

# User access
dotnet run -- discover --api-key demo-user-key

# Premium user access
dotnet run -- discover --api-key demo-premium-key

# Admin access
dotnet run -- discover --api-key demo-admin-key
```

**Expected Behavior:**
- Guest users see only public tools
- User role sees public + user tools
- Premium role sees public + user + premium tools
- Admin role sees all tools

### Tool Execution

Execute specific tools with different authentication levels:

```bash
# Execute public tool (no auth required)
dotnet run -- execute --tool echo --args '{"message":"Hello World"}'

# Execute user tool
dotnet run -- execute --tool get_user_profile --api-key demo-user-key

# Execute premium tool
dotnet run -- execute --tool premium_generate_secure_random --args '{"byteCount":32}' --api-key demo-premium-key

# Execute admin tool
dotnet run -- execute --tool admin_get_system_diagnostics --api-key demo-admin-key

# Test authorization failure
dotnet run -- execute --tool admin_get_system_diagnostics --api-key demo-user-key
```

### Comprehensive Testing

Run a full test suite covering all functionality:

```bash
dotnet run -- test --url http://localhost:8080 --verbose
```

**Test Categories:**
- Health check validation
- Authentication method testing
- Role-based access control verification
- Tool execution success/failure scenarios
- Error handling validation
- Rate limiting enforcement

**Expected Output:**
```
🧪 Running comprehensive test suite...

📊 Test Results:
   Total: 15
   Passed: 14 ✅
   Failed: 1 ❌

📋 Detailed Results:
   ✅ Health Check
   ✅ API Key Authentication - Guest
   ✅ API Key Authentication - User
   ✅ API Key Authentication - Premium
   ✅ API Key Authentication - Admin
   ✅ Hierarchical Role Access
   ✅ Role Restriction Enforcement
   ✅ Public Tool Execution
   ✅ Authenticated Tool Execution
   ✅ Premium Tool Execution
   ✅ Invalid Tool Handling
   ✅ Invalid Arguments Handling
   ✅ Invalid API Key Handling
   ✅ Rate Limiting Enforcement
   ✅ Rate Limiting Allows Some Requests

🎯 Success Rate: 93.3%
```

### Performance Testing

Test server performance under load:

```bash
# Basic performance test
dotnet run -- perf

# Custom load test
dotnet run -- perf --users 10 --requests 50

# Stress test
dotnet run -- perf --users 20 --requests 100 --url http://localhost:8080
```

**Expected Output:**
```
🚀 Running performance tests...
   Users: 10
   Requests per user: 50
   Total requests: 500

📈 Performance Results:
   Total Requests: 500
   Successful: 485
   Failed: 15

⏱️  Response Times:
   Average: 45.67 ms
   Minimum: 12 ms
   Maximum: 234 ms
   95th percentile: 156 ms

🔥 Throughput: 127.32 requests/second
🎉 Excellent performance!
```

### Interactive Demo

Run a guided demonstration:

```bash
dotnet run -- demo
```

This command provides a step-by-step walkthrough of the server's capabilities, showing:
1. Server health verification
2. Role-based access control demonstration
3. Tool execution examples
4. Authorization enforcement

## Integration Examples

### Custom Client Development

Use the test client as a reference for developing your own MCP clients:

```csharp
using DynamicToolFiltering.TestClient;

// Create client instance
using var client = new TestClient("http://localhost:8080");

// Authenticate
client.SetApiKey("demo-user-key");

// Discover tools
var tools = await client.DiscoverToolsAsync();
Console.WriteLine($"Available tools: {string.Join(", ", tools.Select(t => t.Name))}");

// Execute tool
var result = await client.ExecuteToolAsync("echo", new { message = "Hello from custom client!" });
if (result.Success)
{
    Console.WriteLine($"Tool response: {result.Content}");
}
```

### Automated Testing Integration

Integrate with test frameworks:

```csharp
[Test]
public async Task TestServerHealth()
{
    using var client = new TestClient("http://localhost:8080");
    var health = await client.CheckHealthAsync();
    Assert.AreEqual("healthy", health.Status);
}

[Test]
public async Task TestRoleBasedAccess()
{
    using var client = new TestClient("http://localhost:8080");
    
    // User should see limited tools
    client.SetApiKey("demo-user-key");
    var userTools = await client.DiscoverToolsAsync();
    
    // Admin should see more tools
    client.SetApiKey("demo-admin-key");
    var adminTools = await client.DiscoverToolsAsync();
    
    Assert.GreaterOrEqual(adminTools.Count, userTools.Count);
}
```

### CI/CD Integration

Use in continuous integration pipelines:

```yaml
# .github/workflows/integration-test.yml
name: Integration Tests

on: [push, pull_request]

jobs:
  integration-test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '9.0.x'
    
    - name: Start MCP Server
      run: |
        cd samples/DynamicToolFiltering
        dotnet run --launch-profile DevelopmentMode &
        sleep 30
    
    - name: Run Integration Tests
      run: |
        cd samples/DynamicToolFiltering/clients
        dotnet run -- test --url http://localhost:8080
    
    - name: Run Performance Tests
      run: |
        cd samples/DynamicToolFiltering/clients
        dotnet run -- perf --users 5 --requests 20
```

## Advanced Usage

### Custom Authentication

Extend the client for custom authentication methods:

```csharp
// JWT token authentication
client.SetBearerToken("your-jwt-token");

// Custom header authentication
// (Modify TestClient.cs to add custom auth methods)
```

### Performance Monitoring

Use the client for continuous performance monitoring:

```csharp
// Monitor response times over time
var results = await client.RunPerformanceTestsAsync(concurrentUsers: 5, requestsPerUser: 20);

// Alert if performance degrades
if (results.AverageResponseTime > 500)
{
    SendAlert($"Performance degraded: {results.AverageResponseTime}ms average response time");
}
```

### Load Testing

Scale up for serious load testing:

```bash
# Heavy load test
dotnet run -- perf --users 50 --requests 100

# Sustained load test (run multiple instances)
for i in {1..5}; do
    dotnet run -- perf --users 10 --requests 50 &
done
wait
```

## Troubleshooting

### Common Issues

1. **Connection Refused**
   ```
   ❌ Health check failed: Connection refused
   ```
   - Ensure the MCP server is running
   - Verify the URL and port
   - Check firewall settings

2. **Authentication Failures**
   ```
   ❌ Tool discovery failed: Unauthorized
   ```
   - Verify API key is correct
   - Check server authentication configuration
   - Ensure role mappings are configured

3. **Performance Issues**
   ```
   ⚠️ Performance could be improved
   ```
   - Check server resource usage
   - Verify rate limiting settings
   - Consider server scaling

### Debug Mode

Enable verbose logging for troubleshooting:

```bash
dotnet run -- test --verbose
dotnet run -- perf --verbose --users 2 --requests 5
```

### Network Issues

Test with different server URLs:

```bash
# Local development
dotnet run -- health --url http://localhost:8080

# Docker container
dotnet run -- health --url http://localhost:9080

# Remote server
dotnet run -- health --url https://your-server.com
```

## Development

### Building from Source

```bash
git clone https://github.com/microsoft/mcp-csharp-sdk.git
cd mcp-csharp-sdk/samples/DynamicToolFiltering/clients
dotnet build
```

### Adding New Test Scenarios

Extend the test client by adding methods to `TestClient.cs`:

```csharp
public async Task<bool> TestCustomScenarioAsync()
{
    // Your custom test logic
    return true;
}
```

Then integrate into the comprehensive test suite.

### Contributing

1. Follow the existing code patterns
2. Add comprehensive error handling
3. Include logging for debugging
4. Update documentation for new features
5. Test with various server configurations

## License

This test client is part of the MCP C# SDK sample collection and follows the same license terms as the main project.