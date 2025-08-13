using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicToolFiltering.TestClient;

/// <summary>
/// Comprehensive test client for the Dynamic Tool Filtering MCP server.
/// Demonstrates various authentication methods, tool discovery, and execution patterns.
/// 
/// USAGE SCENARIOS:
/// 1. Integration testing - Verify server behavior programmatically
/// 2. Load testing - Generate realistic traffic patterns
/// 3. API exploration - Understand server capabilities
/// 4. Client development - Reference implementation for MCP clients
/// </summary>
public class TestClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<TestClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public TestClient(string baseUrl = "http://localhost:8080", ILogger<TestClient>? logger = null)
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _logger = logger ?? CreateConsoleLogger();
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    #region Authentication Methods

    /// <summary>
    /// Authenticate using API key authentication.
    /// This is the primary authentication method demonstrated in the sample.
    /// </summary>
    public void SetApiKey(string apiKey)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        _logger.LogInformation("API key authentication configured");
    }

    /// <summary>
    /// Authenticate using JWT Bearer token.
    /// Demonstrates integration with OAuth2/OIDC providers.
    /// </summary>
    public void SetBearerToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        _logger.LogInformation("Bearer token authentication configured");
    }

    /// <summary>
    /// Clear all authentication headers.
    /// Useful for testing unauthenticated access scenarios.
    /// </summary>
    public void ClearAuthentication()
    {
        _httpClient.DefaultRequestHeaders.Remove("X-API-Key");
        _httpClient.DefaultRequestHeaders.Authorization = null;
        _logger.LogInformation("Authentication cleared");
    }

    #endregion

    #region Server Health and Discovery

    /// <summary>
    /// Check server health and retrieve basic information.
    /// Should always be the first call to verify server availability.
    /// </summary>
    public async Task<HealthResponse> CheckHealthAsync()
    {
        _logger.LogInformation("Checking server health...");
        
        try
        {
            var response = await _httpClient.GetAsync("/health");
            response.EnsureSuccessStatusCode();
            
            var health = await response.Content.ReadFromJsonAsync<HealthResponse>(_jsonOptions);
            _logger.LogInformation("Server health: {Status} (Environment: {Environment})", 
                health?.Status, health?.Environment);
            
            return health ?? throw new InvalidOperationException("Invalid health response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            throw;
        }
    }

    /// <summary>
    /// Discover available tools for the authenticated user.
    /// Tool visibility varies based on user role and filter configuration.
    /// </summary>
    public async Task<List<ToolInfo>> DiscoverToolsAsync()
    {
        _logger.LogInformation("Discovering available tools...");
        
        try
        {
            var response = await _httpClient.GetAsync("/mcp/v1/tools");
            response.EnsureSuccessStatusCode();
            
            var toolsResponse = await response.Content.ReadFromJsonAsync<ToolsResponse>(_jsonOptions);
            var tools = toolsResponse?.Result?.Tools ?? new List<ToolInfo>();
            
            _logger.LogInformation("Discovered {Count} tools: {ToolNames}", 
                tools.Count, string.Join(", ", tools.Select(t => t.Name)));
            
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool discovery failed");
            throw;
        }
    }

    #endregion

    #region Tool Execution

    /// <summary>
    /// Execute a tool with the provided arguments.
    /// Demonstrates the core MCP tool execution pattern.
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteToolAsync(string toolName, object? arguments = null)
    {
        _logger.LogInformation("Executing tool: {ToolName}", toolName);
        
        try
        {
            var request = new ToolExecutionRequest
            {
                Name = toolName,
                Arguments = arguments ?? new { }
            };
            
            var response = await _httpClient.PostAsJsonAsync("/mcp/v1/tools/call", request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Tool execution failed: {StatusCode} - {Content}", 
                    response.StatusCode, errorContent);
                
                return new ToolExecutionResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    ErrorMessage = errorContent
                };
            }
            
            var resultContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Tool execution successful: {ToolName}", toolName);
            
            return new ToolExecutionResult
            {
                Success = true,
                StatusCode = 200,
                Content = resultContent
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return new ToolExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    #endregion

    #region Test Scenarios

    /// <summary>
    /// Run a comprehensive test suite covering all major functionality.
    /// Useful for integration testing and validation.
    /// </summary>
    public async Task<TestResults> RunComprehensiveTestsAsync()
    {
        var results = new TestResults();
        
        _logger.LogInformation("Starting comprehensive test suite...");
        
        // Test 1: Health Check
        results.AddTest("Health Check", await TestHealthAsync());
        
        // Test 2: Unauthenticated Access
        results.AddTest("Unauthenticated Tool Discovery", await TestUnauthenticatedAccessAsync());
        
        // Test 3: Authentication Methods
        await TestAuthenticationMethodsAsync(results);
        
        // Test 4: Role-based Access Control
        await TestRoleBasedAccessAsync(results);
        
        // Test 5: Tool Execution
        await TestToolExecutionAsync(results);
        
        // Test 6: Error Handling
        await TestErrorHandlingAsync(results);
        
        // Test 7: Rate Limiting (if enabled)
        await TestRateLimitingAsync(results);
        
        _logger.LogInformation("Test suite completed: {Passed}/{Total} tests passed", 
            results.PassedCount, results.TotalCount);
        
        return results;
    }

    private async Task<bool> TestHealthAsync()
    {
        try
        {
            var health = await CheckHealthAsync();
            return health.Status == "healthy";
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TestUnauthenticatedAccessAsync()
    {
        ClearAuthentication();
        try
        {
            var tools = await DiscoverToolsAsync();
            // Should be able to discover at least public tools
            return tools.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task TestAuthenticationMethodsAsync(TestResults results)
    {
        var apiKeys = new Dictionary<string, string>
        {
            ["Guest"] = "demo-guest-key",
            ["User"] = "demo-user-key",
            ["Premium"] = "demo-premium-key",
            ["Admin"] = "demo-admin-key"
        };

        foreach (var (role, apiKey) in apiKeys)
        {
            SetApiKey(apiKey);
            var success = await TestToolDiscoveryAsync();
            results.AddTest($"API Key Authentication - {role}", success);
        }
    }

    private async Task<bool> TestToolDiscoveryAsync()
    {
        try
        {
            var tools = await DiscoverToolsAsync();
            return tools.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task TestRoleBasedAccessAsync(TestResults results)
    {
        // Test hierarchical access - admin should see more tools than user
        SetApiKey("demo-user-key");
        var userTools = await DiscoverToolsAsync();
        
        SetApiKey("demo-admin-key");
        var adminTools = await DiscoverToolsAsync();
        
        results.AddTest("Hierarchical Role Access", adminTools.Count >= userTools.Count);
        
        // Test role restrictions - user should not access admin tools
        SetApiKey("demo-user-key");
        var adminToolResult = await ExecuteToolAsync("admin_get_system_diagnostics");
        results.AddTest("Role Restriction Enforcement", !adminToolResult.Success);
    }

    private async Task TestToolExecutionAsync(TestResults results)
    {
        // Test public tool execution
        ClearAuthentication();
        var echoResult = await ExecuteToolAsync("echo", new { message = "test" });
        results.AddTest("Public Tool Execution", echoResult.Success);
        
        // Test authenticated tool execution
        SetApiKey("demo-user-key");
        var profileResult = await ExecuteToolAsync("get_user_profile");
        results.AddTest("Authenticated Tool Execution", profileResult.Success);
        
        // Test premium tool execution
        SetApiKey("demo-premium-key");
        var premiumResult = await ExecuteToolAsync("premium_generate_secure_random", new { byteCount = 16 });
        results.AddTest("Premium Tool Execution", premiumResult.Success);
    }

    private async Task TestErrorHandlingAsync(TestResults results)
    {
        // Test invalid tool name
        SetApiKey("demo-user-key");
        var invalidToolResult = await ExecuteToolAsync("nonexistent_tool");
        results.AddTest("Invalid Tool Handling", !invalidToolResult.Success);
        
        // Test invalid arguments
        var invalidArgsResult = await ExecuteToolAsync("echo", new { invalid_argument = "test" });
        results.AddTest("Invalid Arguments Handling", !invalidArgsResult.Success);
        
        // Test invalid API key
        SetApiKey("invalid-key");
        var invalidKeyResult = await ExecuteToolAsync("echo", new { message = "test" });
        results.AddTest("Invalid API Key Handling", !invalidKeyResult.Success);
    }

    private async Task TestRateLimitingAsync(TestResults results)
    {
        SetApiKey("demo-guest-key");
        var successCount = 0;
        var rateLimitedCount = 0;
        
        // Make multiple rapid requests to trigger rate limiting
        for (int i = 0; i < 25; i++)
        {
            var result = await ExecuteToolAsync("echo", new { message = $"rate test {i}" });
            if (result.Success)
                successCount++;
            else if (result.StatusCode == 429)
                rateLimitedCount++;
            
            await Task.Delay(50); // Small delay to avoid overwhelming the server
        }
        
        // Rate limiting should trigger for guest users with heavy usage
        results.AddTest("Rate Limiting Enforcement", rateLimitedCount > 0);
        results.AddTest("Rate Limiting Allows Some Requests", successCount > 0);
    }

    #endregion

    #region Performance Testing

    /// <summary>
    /// Run performance tests to measure response times and throughput.
    /// Useful for load testing and performance benchmarking.
    /// </summary>
    public async Task<PerformanceResults> RunPerformanceTestsAsync(int concurrentUsers = 5, int requestsPerUser = 20)
    {
        _logger.LogInformation("Starting performance tests: {Users} users, {Requests} requests each", 
            concurrentUsers, requestsPerUser);
        
        var tasks = new List<Task<UserPerformanceResult>>();
        
        for (int i = 0; i < concurrentUsers; i++)
        {
            var userId = i;
            tasks.Add(RunUserPerformanceTestAsync(userId, requestsPerUser));
        }
        
        var userResults = await Task.WhenAll(tasks);
        
        var overallResults = new PerformanceResults
        {
            ConcurrentUsers = concurrentUsers,
            TotalRequests = concurrentUsers * requestsPerUser,
            UserResults = userResults.ToList()
        };
        
        overallResults.CalculateStatistics();
        
        _logger.LogInformation("Performance test completed: {TotalRequests} requests, " +
                              "avg response time: {AvgResponseTime}ms, " +
                              "95th percentile: {P95ResponseTime}ms",
            overallResults.TotalRequests,
            overallResults.AverageResponseTime,
            overallResults.P95ResponseTime);
        
        return overallResults;
    }

    private async Task<UserPerformanceResult> RunUserPerformanceTestAsync(int userId, int requestCount)
    {
        var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        client.DefaultRequestHeaders.Add("X-API-Key", "demo-user-key");
        
        var result = new UserPerformanceResult { UserId = userId };
        
        for (int i = 0; i < requestCount; i++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var response = await client.PostAsJsonAsync("/mcp/v1/tools/call", 
                    new ToolExecutionRequest
                    {
                        Name = "echo",
                        Arguments = new { message = $"perf test user {userId} request {i}" }
                    });
                
                stopwatch.Stop();
                
                result.ResponseTimes.Add(stopwatch.ElapsedMilliseconds);
                
                if (response.IsSuccessStatusCode)
                    result.SuccessfulRequests++;
                else
                    result.FailedRequests++;
            }
            catch
            {
                stopwatch.Stop();
                result.FailedRequests++;
            }
        }
        
        client.Dispose();
        return result;
    }

    #endregion

    #region Utility Methods

    private static ILogger<TestClient> CreateConsoleLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        return loggerFactory.CreateLogger<TestClient>();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    #endregion
}

#region Data Models

public record HealthResponse(string Status, string Environment, string Version, DateTime Timestamp);

public record ToolInfo(string Name, string Description, object? InputSchema = null);

public record ToolsResponse(ToolsResult Result);
public record ToolsResult(List<ToolInfo> Tools);

public record ToolExecutionRequest
{
    public string Name { get; init; } = "";
    public object Arguments { get; init; } = new { };
}

public record ToolExecutionResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; }
    public string? Content { get; init; }
    public string? ErrorMessage { get; init; }
}

public class TestResults
{
    private readonly List<(string Name, bool Passed)> _tests = new();
    
    public void AddTest(string name, bool passed)
    {
        _tests.Add((name, passed));
    }
    
    public int TotalCount => _tests.Count;
    public int PassedCount => _tests.Count(t => t.Passed);
    public int FailedCount => _tests.Count(t => !t.Passed);
    
    public IReadOnlyList<(string Name, bool Passed)> Tests => _tests.AsReadOnly();
}

public record UserPerformanceResult
{
    public int UserId { get; init; }
    public List<long> ResponseTimes { get; } = new();
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
}

public class PerformanceResults
{
    public int ConcurrentUsers { get; init; }
    public int TotalRequests { get; init; }
    public List<UserPerformanceResult> UserResults { get; init; } = new();
    
    public double AverageResponseTime { get; private set; }
    public long P95ResponseTime { get; private set; }
    public long MinResponseTime { get; private set; }
    public long MaxResponseTime { get; private set; }
    public double ThroughputPerSecond { get; private set; }
    
    public void CalculateStatistics()
    {
        var allResponseTimes = UserResults.SelectMany(r => r.ResponseTimes).ToList();
        
        if (allResponseTimes.Count > 0)
        {
            allResponseTimes.Sort();
            
            AverageResponseTime = allResponseTimes.Average();
            MinResponseTime = allResponseTimes.First();
            MaxResponseTime = allResponseTimes.Last();
            
            var p95Index = (int)(allResponseTimes.Count * 0.95);
            P95ResponseTime = allResponseTimes[Math.Min(p95Index, allResponseTimes.Count - 1)];
            
            var totalSuccessful = UserResults.Sum(r => r.SuccessfulRequests);
            var totalTime = allResponseTimes.Sum() / 1000.0; // Convert to seconds
            ThroughputPerSecond = totalTime > 0 ? totalSuccessful / totalTime : 0;
        }
    }
}

#endregion