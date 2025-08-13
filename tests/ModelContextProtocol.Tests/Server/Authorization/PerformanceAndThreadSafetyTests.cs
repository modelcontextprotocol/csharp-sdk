using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Performance and thread safety tests for the tool filtering system.
/// </summary>
public class PerformanceAndThreadSafetyTests : LoggedTest
{
    public PerformanceAndThreadSafetyTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task ToolAuthorizationService_HighVolumeFiltering_PerformsWithinExpectedTime()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        service.RegisterFilter(new AllowAllToolFilter());
        service.RegisterFilter(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false));
        service.RegisterFilter(new PerformanceTestFilter());

        // Create a large number of tools
        const int toolCount = 10000;
        var tools = Enumerable.Range(0, toolCount)
            .Select(i => CreateTestTool($"tool_{i}", $"Test tool number {i}"))
            .ToArray();

        var context = CreateTestContext();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        TestOutputHelper.WriteLine($"Filtered {toolCount} tools in {elapsedMs}ms ({toolCount / (double)elapsedMs * 1000:F0} tools/second)");
        
        // Performance assertion: should process at least 1000 tools per second
        Assert.True(elapsedMs < toolCount / 10, $"Performance too slow: {elapsedMs}ms for {toolCount} tools");
        
        // Verify correctness
        Assert.True(filteredTools.Count() < toolCount, "Some tools should be filtered out");
        Assert.All(filteredTools, tool => Assert.DoesNotContain("admin_", tool.Name));
    }

    [Fact]
    public async Task ToolAuthorizationService_HighVolumeExecution_PerformsWithinExpectedTime()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        service.RegisterFilter(new AllowAllToolFilter());
        service.RegisterFilter(new PerformanceTestFilter());

        const int operationCount = 10000;
        var context = CreateTestContext();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = Enumerable.Range(0, operationCount)
            .Select(i => service.AuthorizeToolExecutionAsync($"tool_{i}", context))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        stopwatch.Stop();
        var elapsedMs = stopwatch.ElapsedMilliseconds;
        
        TestOutputHelper.WriteLine($"Authorized {operationCount} tool executions in {elapsedMs}ms ({operationCount / (double)elapsedMs * 1000:F0} operations/second)");
        
        // Performance assertion: should process at least 2000 operations per second
        Assert.True(elapsedMs < operationCount / 20, $"Performance too slow: {elapsedMs}ms for {operationCount} operations");
        
        // Verify correctness
        Assert.All(results, result => Assert.True(result.IsAuthorized));
    }

    [Fact]
    public async Task ToolAuthorizationService_ConcurrentRegistration_IsThreadSafe()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        const int concurrentOperations = 100;
        const int filtersPerOperation = 10;
        
        var allFilters = new List<IToolFilter>();
        var registrationTasks = new List<Task>();

        // Act - Register filters concurrently
        for (int i = 0; i < concurrentOperations; i++)
        {
            var operationIndex = i;
            var task = Task.Run(() =>
            {
                for (int j = 0; j < filtersPerOperation; j++)
                {
                    var filter = new ConcurrentTestFilter($"Filter_{operationIndex}_{j}", priority: operationIndex * 100 + j);
                    service.RegisterFilter(filter);
                    lock (allFilters)
                    {
                        allFilters.Add(filter);
                    }
                }
            });
            registrationTasks.Add(task);
        }

        await Task.WhenAll(registrationTasks);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Equal(concurrentOperations * filtersPerOperation, registeredFilters.Count);
        
        // Verify all filters were registered
        foreach (var filter in allFilters)
        {
            Assert.Contains(filter, registeredFilters);
        }
    }

    [Fact]
    public async Task ToolAuthorizationService_ConcurrentFiltering_IsThreadSafe()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Add filters with different behaviors
        service.RegisterFilter(new ConcurrentTestFilter("AllowFilter", allowAll: true, priority: 1));
        service.RegisterFilter(new ConcurrentTestFilter("PatternFilter", allowAll: false, priority: 2));
        service.RegisterFilter(new ThreadSafeCountingFilter(priority: 3));

        var tools = Enumerable.Range(0, 1000)
            .Select(i => CreateTestTool($"tool_{i}"))
            .ToArray();

        const int concurrentOperations = 50;
        var contexts = Enumerable.Range(0, concurrentOperations)
            .Select(i => CreateTestContext($"session_{i}", $"user_{i}"))
            .ToArray();

        // Act - Filter tools concurrently
        var filteringTasks = contexts.Select(context => 
            service.FilterToolsAsync(tools, context)).ToArray();

        var results = await Task.WhenAll(filteringTasks);

        // Assert
        Assert.All(results, result => 
        {
            Assert.NotNull(result);
            Assert.True(result.Any(), "Should have some tools after filtering");
        });
        
        // Verify thread-safe counter
        var countingFilter = service.GetRegisteredFilters()
            .OfType<ThreadSafeCountingFilter>()
            .First();
        
        // Should have been called for each tool in each context
        var expectedCallCount = tools.Length * concurrentOperations;
        Assert.Equal(expectedCallCount, countingFilter.CallCount);
    }

    [Fact]
    public async Task ToolAuthorizationService_ConcurrentExecutionAuthorization_IsThreadSafe()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        service.RegisterFilter(new ThreadSafeCountingFilter());
        service.RegisterFilter(new ConcurrentAuthorizationFilter());

        const int concurrentOperations = 100;
        const int operationsPerThread = 100;
        
        var contexts = Enumerable.Range(0, concurrentOperations)
            .Select(i => CreateTestContext($"session_{i}", $"user_{i}"))
            .ToArray();

        // Act - Authorize tool executions concurrently
        var authorizationTasks = new List<Task<AuthorizationResult[]>>();
        
        for (int i = 0; i < concurrentOperations; i++)
        {
            var context = contexts[i];
            var task = Task.Run(async () =>
            {
                var results = new AuthorizationResult[operationsPerThread];
                for (int j = 0; j < operationsPerThread; j++)
                {
                    results[j] = await service.AuthorizeToolExecutionAsync($"tool_{i}_{j}", context);
                }
                return results;
            });
            authorizationTasks.Add(task);
        }

        var allResults = await Task.WhenAll(authorizationTasks);

        // Assert
        var flatResults = allResults.SelectMany(r => r).ToArray();
        Assert.Equal(concurrentOperations * operationsPerThread, flatResults.Length);
        
        // Verify all operations completed successfully (no exceptions)
        Assert.All(flatResults, result => Assert.NotNull(result));
        
        // Verify thread-safe counter
        var countingFilter = service.GetRegisteredFilters()
            .OfType<ThreadSafeCountingFilter>()
            .First();
        
        Assert.Equal(concurrentOperations * operationsPerThread, countingFilter.ExecutionCallCount);
    }

    [Fact]
    public async Task ToolAuthorizationService_UnderLoad_MaintainsCorrectness()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Add filters with complex logic
        service.RegisterFilter(new LoadTestFilter("admin", priority: 1));
        service.RegisterFilter(new LoadTestFilter("user", priority: 2));
        service.RegisterFilter(new LoadTestFilter("guest", priority: 3));

        var tools = new[]
        {
            CreateTestTool("admin_tool"),
            CreateTestTool("user_tool"), 
            CreateTestTool("guest_tool"),
            CreateTestTool("public_tool")
        };

        const int concurrentUsers = 50;
        const int operationsPerUser = 50;

        // Create contexts for different user types
        var adminContexts = Enumerable.Range(0, concurrentUsers / 3)
            .Select(i => CreateTestContext($"admin_session_{i}", $"admin_{i}", new[] { "admin" }))
            .ToArray();
        
        var userContexts = Enumerable.Range(0, concurrentUsers / 3)
            .Select(i => CreateTestContext($"user_session_{i}", $"user_{i}", new[] { "user" }))
            .ToArray();
        
        var guestContexts = Enumerable.Range(0, concurrentUsers / 3)
            .Select(i => CreateTestContext($"guest_session_{i}", $"guest_{i}", new[] { "guest" }))
            .ToArray();

        var allContexts = adminContexts.Concat(userContexts).Concat(guestContexts).ToArray();

        // Act - Simulate load
        var loadTasks = allContexts.Select(async context =>
        {
            var results = new List<AuthorizationResult>();
            
            for (int i = 0; i < operationsPerUser; i++)
            {
                foreach (var tool in tools)
                {
                    var result = await service.AuthorizeToolExecutionAsync(tool.Name, context);
                    results.Add(result);
                }
            }
            
            return new { Context = context, Results = results };
        }).ToArray();

        var loadResults = await Task.WhenAll(loadTasks);

        // Assert correctness under load
        foreach (var userResult in loadResults)
        {
            var userRoles = userResult.Context.UserRoles;
            
            foreach (var result in userResult.Results)
            {
                // Verify authorization logic is correctly applied
                if (userRoles.Contains("admin"))
                {
                    Assert.True(result.IsAuthorized, "Admin should have access to all tools");
                }
                else if (userRoles.Contains("user"))
                {
                    // Users should not have access to admin tools
                    if (result.Reason?.Contains("admin_tool") == true)
                    {
                        Assert.False(result.IsAuthorized, "Users should not access admin tools");
                    }
                }
                else if (userRoles.Contains("guest"))
                {
                    // Guests should only have access to guest and public tools
                    if (result.Reason?.Contains("admin_tool") == true || result.Reason?.Contains("user_tool") == true)
                    {
                        Assert.False(result.IsAuthorized, "Guests should have limited access");
                    }
                }
            }
        }
    }

    [Fact]
    public void ToolAuthorizationService_MemoryUsage_StaysWithinReasonableBounds()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Force initial garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);

        // Act - Register many filters and perform operations
        const int filterCount = 1000;
        for (int i = 0; i < filterCount; i++)
        {
            service.RegisterFilter(new MemoryTestFilter(i));
        }

        // Perform operations that might leak memory
        var tools = Enumerable.Range(0, 1000)
            .Select(i => CreateTestTool($"tool_{i}"))
            .ToArray();
        
        var context = CreateTestContext();
        
        // Run filtering operations multiple times
        for (int iteration = 0; iteration < 10; iteration++)
        {
            _ = service.FilterToolsAsync(tools, context).GetAwaiter().GetResult();
        }

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - Memory increase should be reasonable
        const long maxReasonableIncrease = 50 * 1024 * 1024; // 50MB
        TestOutputHelper.WriteLine($"Memory increase: {memoryIncrease / 1024.0 / 1024.0:F2} MB");
        
        Assert.True(memoryIncrease < maxReasonableIncrease, 
            $"Memory usage increased by {memoryIncrease / 1024.0 / 1024.0:F2} MB, which exceeds the limit of {maxReasonableIncrease / 1024.0 / 1024.0:F2} MB");
    }

    [Fact]
    public async Task ToolFilterAggregator_ConcurrentServiceResolution_IsThreadSafe()
    {
        // This test would require dependency injection setup
        // For now, we'll test the core thread safety of filter aggregation
        
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Add filters that will be accessed concurrently
        var sharedFilter = new ConcurrentAccessTestFilter();
        service.RegisterFilter(sharedFilter);
        service.RegisterFilter(new AllowAllToolFilter());

        var tools = Enumerable.Range(0, 100)
            .Select(i => CreateTestTool($"tool_{i}"))
            .ToArray();

        const int concurrentThreads = 20;
        var contexts = Enumerable.Range(0, concurrentThreads)
            .Select(i => CreateTestContext($"session_{i}"))
            .ToArray();

        // Act - Access the same filter from multiple threads
        var concurrentTasks = contexts.Select(context =>
            Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    await service.FilterToolsAsync(tools, context);
                    await service.AuthorizeToolExecutionAsync($"test_tool_{i}", context);
                }
            })).ToArray();

        await Task.WhenAll(concurrentTasks);

        // Assert - No exceptions should occur and state should be consistent
        Assert.True(sharedFilter.TotalCalls > 0);
        Assert.Equal(0, sharedFilter.ErrorCount);
    }

    private static Tool CreateTestTool(string name, string? description = null)
    {
        return new Tool
        {
            Name = name,
            Description = description ?? $"Test tool: {name}",
            InputSchema = new JsonObject()
        };
    }

    private static ToolAuthorizationContext CreateTestContext(
        string? sessionId = null, 
        string? userId = null, 
        IEnumerable<string>? roles = null)
    {
        var context = ToolAuthorizationContext.ForSession(sessionId ?? "test-session");
        if (userId != null)
        {
            context.UserId = userId;
        }
        if (roles != null)
        {
            foreach (var role in roles)
            {
                context.UserRoles.Add(role);
            }
        }
        return context;
    }

    // Test filter implementations for performance and concurrency testing

    private class PerformanceTestFilter : IToolFilter
    {
        public int Priority => 50;

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Simulate some work without being too slow
            var hash = tool.Name.GetHashCode();
            return Task.FromResult(hash % 10 != 0); // Filter out ~10% of tools
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var hash = toolName.GetHashCode();
            return Task.FromResult(hash % 10 != 0 
                ? AuthorizationResult.Allow("Performance test passed") 
                : AuthorizationResult.Deny("Performance test filtered"));
        }
    }

    private class ConcurrentTestFilter : IToolFilter
    {
        private readonly string _name;
        private readonly bool _allowAll;

        public ConcurrentTestFilter(string name, bool allowAll = true, int priority = 100)
        {
            _name = name;
            _allowAll = allowAll;
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_allowAll || !tool.Name.Contains("filtered"));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_allowAll || !toolName.Contains("filtered")
                ? AuthorizationResult.Allow($"Allowed by {_name}")
                : AuthorizationResult.Deny($"Denied by {_name}"));
        }
    }

    private class ThreadSafeCountingFilter : IToolFilter
    {
        private long _callCount;
        private long _executionCallCount;

        public ThreadSafeCountingFilter(int priority = 100)
        {
            Priority = priority;
        }

        public int Priority { get; }
        public long CallCount => _callCount;
        public long ExecutionCallCount => _executionCallCount;

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCallCount);
            return Task.FromResult(AuthorizationResult.Allow("Thread-safe counting"));
        }
    }

    private class ConcurrentAuthorizationFilter : IToolFilter
    {
        private readonly ConcurrentDictionary<string, int> _callCounts = new();

        public int Priority => 100;

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var result = await CanExecuteToolAsync(tool.Name, context, cancellationToken);
            return result.IsAuthorized;
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            _callCounts.AddOrUpdate(toolName, 1, (key, count) => count + 1);
            
            // Simulate some processing time
            Thread.SpinWait(100);
            
            return Task.FromResult(AuthorizationResult.Allow("Concurrent authorization completed"));
        }
    }

    private class LoadTestFilter : IToolFilter
    {
        private readonly string _requiredRole;

        public LoadTestFilter(string requiredRole, int priority = 100)
        {
            _requiredRole = requiredRole;
            Priority = priority;
        }

        public int Priority { get; }

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var result = await CanExecuteToolAsync(tool.Name, context, cancellationToken);
            return result.IsAuthorized;
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Complex authorization logic
            if (toolName.StartsWith($"{_requiredRole}_") && !context.UserRoles.Contains(_requiredRole))
            {
                return Task.FromResult(AuthorizationResult.Deny($"Tool {toolName} requires role: {_requiredRole}"));
            }
            
            if (context.UserRoles.Contains("admin"))
            {
                return Task.FromResult(AuthorizationResult.Allow("Admin access"));
            }
            
            if (toolName == "public_tool")
            {
                return Task.FromResult(AuthorizationResult.Allow("Public access"));
            }
            
            return Task.FromResult(context.UserRoles.Contains(_requiredRole)
                ? AuthorizationResult.Allow($"Role-based access: {_requiredRole}")
                : AuthorizationResult.Deny($"Insufficient role for {toolName}"));
        }
    }

    private class MemoryTestFilter : IToolFilter
    {
        private readonly int _id;
        private readonly byte[] _data; // Simulate some memory usage

        public MemoryTestFilter(int id)
        {
            _id = id;
            Priority = id;
            _data = new byte[1024]; // 1KB per filter
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Simulate some computation
            var hash = tool.Name.GetHashCode() ^ _id;
            return Task.FromResult(hash % 100 != 0);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var hash = toolName.GetHashCode() ^ _id;
            return Task.FromResult(hash % 100 != 0
                ? AuthorizationResult.Allow($"Memory test {_id}")
                : AuthorizationResult.Deny($"Memory test {_id} filtered"));
        }
    }

    private class ConcurrentAccessTestFilter : IToolFilter
    {
        private long _totalCalls;
        private long _errorCount;

        public int Priority => 100;
        public long TotalCalls => _totalCalls;
        public long ErrorCount => _errorCount;

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _totalCalls);
            
            try
            {
                // Simulate shared resource access
                var result = ProcessSharedData(tool.Name);
                return Task.FromResult(result);
            }
            catch
            {
                Interlocked.Increment(ref _errorCount);
                return Task.FromResult(false);
            }
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _totalCalls);
            
            try
            {
                var result = ProcessSharedData(toolName);
                return Task.FromResult(result
                    ? AuthorizationResult.Allow("Concurrent access successful")
                    : AuthorizationResult.Deny("Concurrent access filtered"));
            }
            catch
            {
                Interlocked.Increment(ref _errorCount);
                return Task.FromResult(AuthorizationResult.Deny("Concurrent access error"));
            }
        }

        private bool ProcessSharedData(string input)
        {
            // Simulate some shared data processing
            var hash = input.GetHashCode();
            Thread.SpinWait(10); // Small delay to increase chance of race conditions
            return hash % 4 != 0; // Allow 75% of tools
        }
    }
}

/// <summary>
/// Additional performance test utilities.
/// </summary>
public static class PerformanceTestUtilities
{
    /// <summary>
    /// Measures the execution time of an async operation.
    /// </summary>
    public static async Task<(T Result, TimeSpan Duration)> MeasureAsync<T>(Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await operation();
        stopwatch.Stop();
        return (result, stopwatch.Elapsed);
    }

    /// <summary>
    /// Measures the execution time of a sync operation.
    /// </summary>
    public static (T Result, TimeSpan Duration) Measure<T>(Func<T> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = operation();
        stopwatch.Stop();
        return (result, stopwatch.Elapsed);
    }

    /// <summary>
    /// Runs a performance benchmark with multiple iterations.
    /// </summary>
    public static async Task<BenchmarkResult> BenchmarkAsync<T>(
        Func<Task<T>> operation, 
        int iterations = 100, 
        int warmupIterations = 10)
    {
        // Warmup
        for (int i = 0; i < warmupIterations; i++)
        {
            await operation();
        }

        var durations = new List<TimeSpan>();
        
        // Actual benchmark
        for (int i = 0; i < iterations; i++)
        {
            var (_, duration) = await MeasureAsync(operation);
            durations.Add(duration);
        }

        return new BenchmarkResult(durations);
    }

    public class BenchmarkResult
    {
        public BenchmarkResult(IEnumerable<TimeSpan> durations)
        {
            var sortedDurations = durations.OrderBy(d => d).ToArray();
            
            Min = sortedDurations.First();
            Max = sortedDurations.Last();
            Average = TimeSpan.FromTicks((long)sortedDurations.Average(d => d.Ticks));
            Median = sortedDurations[sortedDurations.Length / 2];
            
            // 95th percentile
            var index95 = (int)(sortedDurations.Length * 0.95);
            Percentile95 = sortedDurations[index95];
            
            Iterations = sortedDurations.Length;
        }

        public TimeSpan Min { get; }
        public TimeSpan Max { get; }
        public TimeSpan Average { get; }
        public TimeSpan Median { get; }
        public TimeSpan Percentile95 { get; }
        public int Iterations { get; }

        public override string ToString()
        {
            return $"Benchmark Results ({Iterations} iterations):\n" +
                   $"  Min: {Min.TotalMilliseconds:F2}ms\n" +
                   $"  Max: {Max.TotalMilliseconds:F2}ms\n" +
                   $"  Average: {Average.TotalMilliseconds:F2}ms\n" +
                   $"  Median: {Median.TotalMilliseconds:F2}ms\n" +
                   $"  95th Percentile: {Percentile95.TotalMilliseconds:F2}ms";
        }
    }
}