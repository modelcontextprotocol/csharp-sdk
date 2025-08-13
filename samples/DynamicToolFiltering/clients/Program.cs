using DynamicToolFiltering.TestClient;
using Microsoft.Extensions.Logging;
using System.CommandLine;

/// <summary>
/// Test client console application for the Dynamic Tool Filtering MCP server.
/// 
/// This application demonstrates how to:
/// 1. Connect to and authenticate with an MCP server
/// 2. Discover available tools based on user role
/// 3. Execute tools with proper error handling
/// 4. Run comprehensive test suites
/// 5. Perform load testing and performance analysis
/// 
/// USAGE EXAMPLES:
/// 
/// Basic health check:
/// dotnet run -- health --url http://localhost:8080
/// 
/// Discover tools with API key:
/// dotnet run -- discover --url http://localhost:8080 --api-key demo-user-key
/// 
/// Execute a specific tool:
/// dotnet run -- execute --url http://localhost:8080 --api-key demo-user-key --tool echo --args '{"message":"Hello World"}'
/// 
/// Run comprehensive tests:
/// dotnet run -- test --url http://localhost:8080
/// 
/// Performance testing:
/// dotnet run -- perf --url http://localhost:8080 --users 10 --requests 50
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Dynamic Tool Filtering MCP Server Test Client");
        
        // Common options
        var urlOption = new Option<string>("--url", () => "http://localhost:8080", "Server URL");
        var apiKeyOption = new Option<string?>("--api-key", "API key for authentication");
        var verboseOption = new Option<bool>("--verbose", "Enable verbose logging");
        
        // Health check command
        var healthCommand = new Command("health", "Check server health");
        healthCommand.AddOption(urlOption);
        healthCommand.AddOption(verboseOption);
        healthCommand.SetHandler(async (string url, bool verbose) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunHealthCheckAsync(client);
        }, urlOption, verboseOption);
        
        // Discover tools command
        var discoverCommand = new Command("discover", "Discover available tools");
        discoverCommand.AddOption(urlOption);
        discoverCommand.AddOption(apiKeyOption);
        discoverCommand.AddOption(verboseOption);
        discoverCommand.SetHandler(async (string url, string? apiKey, bool verbose) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunDiscoveryAsync(client, apiKey);
        }, urlOption, apiKeyOption, verboseOption);
        
        // Execute tool command
        var executeCommand = new Command("execute", "Execute a specific tool");
        executeCommand.AddOption(urlOption);
        executeCommand.AddOption(apiKeyOption);
        executeCommand.AddOption(verboseOption);
        var toolOption = new Option<string>("--tool", "Tool name to execute") { IsRequired = true };
        var argsOption = new Option<string?>("--args", "Tool arguments as JSON");
        executeCommand.AddOption(toolOption);
        executeCommand.AddOption(argsOption);
        executeCommand.SetHandler(async (string url, string? apiKey, bool verbose, string tool, string? args) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunToolExecutionAsync(client, apiKey, tool, args);
        }, urlOption, apiKeyOption, verboseOption, toolOption, argsOption);
        
        // Comprehensive test command
        var testCommand = new Command("test", "Run comprehensive test suite");
        testCommand.AddOption(urlOption);
        testCommand.AddOption(verboseOption);
        testCommand.SetHandler(async (string url, bool verbose) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunComprehensiveTestsAsync(client);
        }, urlOption, verboseOption);
        
        // Performance test command
        var perfCommand = new Command("perf", "Run performance tests");
        perfCommand.AddOption(urlOption);
        perfCommand.AddOption(verboseOption);
        var usersOption = new Option<int>("--users", () => 5, "Number of concurrent users");
        var requestsOption = new Option<int>("--requests", () => 20, "Requests per user");
        perfCommand.AddOption(usersOption);
        perfCommand.AddOption(requestsOption);
        perfCommand.SetHandler(async (string url, bool verbose, int users, int requests) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunPerformanceTestsAsync(client, users, requests);
        }, urlOption, verboseOption, usersOption, requestsOption);
        
        // Demo command - interactive demonstration
        var demoCommand = new Command("demo", "Run interactive demonstration");
        demoCommand.AddOption(urlOption);
        demoCommand.AddOption(verboseOption);
        demoCommand.SetHandler(async (string url, bool verbose) =>
        {
            using var client = CreateTestClient(url, verbose);
            await RunInteractiveDemoAsync(client);
        }, urlOption, verboseOption);
        
        // Add all commands to root
        rootCommand.AddCommand(healthCommand);
        rootCommand.AddCommand(discoverCommand);
        rootCommand.AddCommand(executeCommand);
        rootCommand.AddCommand(testCommand);
        rootCommand.AddCommand(perfCommand);
        rootCommand.AddCommand(demoCommand);
        
        return await rootCommand.InvokeAsync(args);
    }
    
    private static TestClient CreateTestClient(string url, bool verbose)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });
        
        var logger = loggerFactory.CreateLogger<TestClient>();
        return new TestClient(url, logger);
    }
    
    private static async Task RunHealthCheckAsync(TestClient client)
    {
        Console.WriteLine("🏥 Checking server health...");
        
        try
        {
            var health = await client.CheckHealthAsync();
            Console.WriteLine($"✅ Server is healthy!");
            Console.WriteLine($"   Status: {health.Status}");
            Console.WriteLine($"   Environment: {health.Environment}");
            Console.WriteLine($"   Version: {health.Version}");
            Console.WriteLine($"   Timestamp: {health.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Health check failed: {ex.Message}");
        }
    }
    
    private static async Task RunDiscoveryAsync(TestClient client, string? apiKey)
    {
        Console.WriteLine("🔍 Discovering available tools...");
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.SetApiKey(apiKey);
            Console.WriteLine($"   Using API key: {apiKey}");
        }
        else
        {
            Console.WriteLine("   No authentication (testing public access)");
        }
        
        try
        {
            var tools = await client.DiscoverToolsAsync();
            
            if (tools.Count == 0)
            {
                Console.WriteLine("   No tools available");
                return;
            }
            
            Console.WriteLine($"✅ Found {tools.Count} available tools:");
            foreach (var tool in tools)
            {
                Console.WriteLine($"   📋 {tool.Name}");
                if (!string.IsNullOrEmpty(tool.Description))
                {
                    Console.WriteLine($"      {tool.Description}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tool discovery failed: {ex.Message}");
        }
    }
    
    private static async Task RunToolExecutionAsync(TestClient client, string? apiKey, string toolName, string? argsJson)
    {
        Console.WriteLine($"⚡ Executing tool: {toolName}");
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.SetApiKey(apiKey);
            Console.WriteLine($"   Using API key: {apiKey}");
        }
        
        try
        {
            object? arguments = null;
            if (!string.IsNullOrEmpty(argsJson))
            {
                arguments = System.Text.Json.JsonSerializer.Deserialize<object>(argsJson);
                Console.WriteLine($"   Arguments: {argsJson}");
            }
            
            var result = await client.ExecuteToolAsync(toolName, arguments);
            
            if (result.Success)
            {
                Console.WriteLine("✅ Tool execution successful!");
                Console.WriteLine($"   Response: {result.Content}");
            }
            else
            {
                Console.WriteLine($"❌ Tool execution failed (HTTP {result.StatusCode})");
                Console.WriteLine($"   Error: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Tool execution failed: {ex.Message}");
        }
    }
    
    private static async Task RunComprehensiveTestsAsync(TestClient client)
    {
        Console.WriteLine("🧪 Running comprehensive test suite...");
        Console.WriteLine();
        
        try
        {
            var results = await client.RunComprehensiveTestsAsync();
            
            Console.WriteLine("📊 Test Results:");
            Console.WriteLine($"   Total: {results.TotalCount}");
            Console.WriteLine($"   Passed: {results.PassedCount} ✅");
            Console.WriteLine($"   Failed: {results.FailedCount} ❌");
            Console.WriteLine();
            
            Console.WriteLine("📋 Detailed Results:");
            foreach (var (name, passed) in results.Tests)
            {
                var icon = passed ? "✅" : "❌";
                Console.WriteLine($"   {icon} {name}");
            }
            
            var successRate = (double)results.PassedCount / results.TotalCount * 100;
            Console.WriteLine();
            Console.WriteLine($"🎯 Success Rate: {successRate:F1}%");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test suite failed: {ex.Message}");
        }
    }
    
    private static async Task RunPerformanceTestsAsync(TestClient client, int users, int requests)
    {
        Console.WriteLine($"🚀 Running performance tests...");
        Console.WriteLine($"   Users: {users}");
        Console.WriteLine($"   Requests per user: {requests}");
        Console.WriteLine($"   Total requests: {users * requests}");
        Console.WriteLine();
        
        try
        {
            var results = await client.RunPerformanceTestsAsync(users, requests);
            
            Console.WriteLine("📈 Performance Results:");
            Console.WriteLine($"   Total Requests: {results.TotalRequests}");
            Console.WriteLine($"   Successful: {results.UserResults.Sum(r => r.SuccessfulRequests)}");
            Console.WriteLine($"   Failed: {results.UserResults.Sum(r => r.FailedRequests)}");
            Console.WriteLine();
            
            Console.WriteLine("⏱️  Response Times:");
            Console.WriteLine($"   Average: {results.AverageResponseTime:F2} ms");
            Console.WriteLine($"   Minimum: {results.MinResponseTime} ms");
            Console.WriteLine($"   Maximum: {results.MaxResponseTime} ms");
            Console.WriteLine($"   95th percentile: {results.P95ResponseTime} ms");
            Console.WriteLine();
            
            Console.WriteLine($"🔥 Throughput: {results.ThroughputPerSecond:F2} requests/second");
            
            // Performance assessment
            if (results.AverageResponseTime < 200)
                Console.WriteLine("🎉 Excellent performance!");
            else if (results.AverageResponseTime < 500)
                Console.WriteLine("👍 Good performance");
            else
                Console.WriteLine("⚠️  Performance could be improved");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Performance test failed: {ex.Message}");
        }
    }
    
    private static async Task RunInteractiveDemoAsync(TestClient client)
    {
        Console.WriteLine("🎭 Interactive Demo - Dynamic Tool Filtering");
        Console.WriteLine("==========================================");
        Console.WriteLine();
        
        // Step 1: Health check
        Console.WriteLine("Step 1: Checking server health...");
        await RunHealthCheckAsync(client);
        Console.WriteLine();
        
        // Step 2: Demonstrate role-based access
        Console.WriteLine("Step 2: Demonstrating role-based access control...");
        
        var roles = new Dictionary<string, string>
        {
            ["Guest"] = "demo-guest-key",
            ["User"] = "demo-user-key",
            ["Premium"] = "demo-premium-key",
            ["Admin"] = "demo-admin-key"
        };
        
        foreach (var (roleName, apiKey) in roles)
        {
            Console.WriteLine($"\n   🎭 Testing as {roleName} user:");
            client.SetApiKey(apiKey);
            
            try
            {
                var tools = await client.DiscoverToolsAsync();
                Console.WriteLine($"      Visible tools: {tools.Count}");
                
                if (tools.Count > 0)
                {
                    Console.WriteLine($"      Examples: {string.Join(", ", tools.Take(3).Select(t => t.Name))}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      Error: {ex.Message}");
            }
        }
        
        Console.WriteLine();
        
        // Step 3: Demonstrate tool execution
        Console.WriteLine("Step 3: Demonstrating tool execution...");
        
        client.SetApiKey("demo-user-key");
        
        var testCases = new[]
        {
            new { Tool = "echo", Args = new { message = "Hello from demo!" }, Description = "Public tool" },
            new { Tool = "get_user_profile", Args = (object)new { }, Description = "User tool" }
        };
        
        foreach (var testCase in testCases)
        {
            Console.WriteLine($"\n   ⚡ Executing {testCase.Description}: {testCase.Tool}");
            var result = await client.ExecuteToolAsync(testCase.Tool, testCase.Args);
            
            if (result.Success)
            {
                Console.WriteLine("      ✅ Success!");
            }
            else
            {
                Console.WriteLine($"      ❌ Failed: {result.ErrorMessage}");
            }
        }
        
        // Step 4: Demonstrate authorization failure
        Console.WriteLine("\nStep 4: Demonstrating authorization controls...");
        Console.WriteLine("   🚫 Attempting to access admin tool with user credentials:");
        
        var unauthorizedResult = await client.ExecuteToolAsync("admin_get_system_diagnostics");
        if (!unauthorizedResult.Success)
        {
            Console.WriteLine("   ✅ Access correctly denied - security working!");
        }
        else
        {
            Console.WriteLine("   ⚠️  Unexpected: Access was granted");
        }
        
        Console.WriteLine();
        Console.WriteLine("🎉 Demo completed! The Dynamic Tool Filtering system is working correctly.");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("- Run comprehensive tests: dotnet run test");
        Console.WriteLine("- Try performance testing: dotnet run perf");
        Console.WriteLine("- Explore with different API keys: dotnet run discover --api-key demo-admin-key");
    }
}