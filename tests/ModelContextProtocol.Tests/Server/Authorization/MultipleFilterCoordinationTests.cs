using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Integration tests for multiple filter coordination scenarios.
/// </summary>
public class MultipleFilterCoordinationTests : LoggedTest
{
    public MultipleFilterCoordinationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task ComplexFilterChain_WithVariousPriorities_AppliesCorrectOrder()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Add filters with different priorities and behaviors
        service.RegisterFilter(new SecurityFilter(priority: 1));           // Highest priority - security checks
        service.RegisterFilter(new RateLimitFilter(priority: 5));           // Rate limiting
        service.RegisterFilter(new RoleBasedFilter(priority: 10));          // Role-based access
        service.RegisterFilter(new FeatureFlagFilter(priority: 15));        // Feature flags
        service.RegisterFilter(new AuditFilter(priority: 100));             // Audit logging (lowest priority)

        var tools = new[]
        {
            CreateTestTool("user_profile"),
            CreateTestTool("admin_delete"),
            CreateTestTool("beta_feature"),
            CreateTestTool("high_rate_tool"),
            CreateTestTool("secure_operation")
        };

        var context = CreateTestContext();

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        // Verify that filters are applied in priority order and expected tools are filtered
        var toolNames = filteredTools.Select(t => t.Name).ToList();
        
        // SecurityFilter should block secure_operation
        Assert.DoesNotContain("secure_operation", toolNames);
        
        // RateLimitFilter should block high_rate_tool
        Assert.DoesNotContain("high_rate_tool", toolNames);
        
        // RoleBasedFilter should block admin_delete (no admin role)
        Assert.DoesNotContain("admin_delete", toolNames);
        
        // FeatureFlagFilter should block beta_feature
        Assert.DoesNotContain("beta_feature", toolNames);
        
        // user_profile should be allowed through all filters
        Assert.Contains("user_profile", toolNames);
        
        // Verify all filters were called in order
        Assert.True(SecurityFilter.CallOrder < RateLimitFilter.CallOrder);
        Assert.True(RateLimitFilter.CallOrder < RoleBasedFilter.CallOrder);
        Assert.True(RoleBasedFilter.CallOrder < FeatureFlagFilter.CallOrder);
        Assert.True(FeatureFlagFilter.CallOrder < AuditFilter.CallOrder);
    }

    [Fact]
    public async Task FilterChain_WithEarlyTermination_StopsProcessingCorrectly()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        var mockFilter1 = new ExecutionTrackingFilter("Filter1", priority: 1, shouldInclude: true);
        var mockFilter2 = new ExecutionTrackingFilter("Filter2", priority: 2, shouldInclude: false); // This will deny
        var mockFilter3 = new ExecutionTrackingFilter("Filter3", priority: 3, shouldInclude: true);
        
        service.RegisterFilter(mockFilter1);
        service.RegisterFilter(mockFilter2);
        service.RegisterFilter(mockFilter3);

        var tools = new[] { CreateTestTool("test_tool") };
        var context = CreateTestContext();

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Empty(filteredTools); // Tool should be filtered out
        
        // Verify execution order
        Assert.True(mockFilter1.WasCalled);
        Assert.True(mockFilter2.WasCalled); // This denies, so processing stops here
        Assert.False(mockFilter3.WasCalled); // This should not be called due to early termination
        
        Assert.True(mockFilter1.CallTime < mockFilter2.CallTime);
    }

    [Fact]
    public async Task FilterChain_WithExceptionHandling_IsolatesFailures()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        service.RegisterFilter(new AllowAllToolFilter(priority: 1));
        service.RegisterFilter(new ExceptionThrowingFilter(priority: 2));
        service.RegisterFilter(new AllowAllToolFilter(priority: 3));

        var tools = new[] { CreateTestTool("test_tool") };
        var context = CreateTestContext();

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        // Exception in middle filter should cause tool to be filtered out
        Assert.Empty(filteredTools);
    }

    [Fact]
    public async Task FilterChain_WithDifferentFilterTypes_CombinesLogicCorrectly()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Blacklist filter: deny admin tools
        service.RegisterFilter(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1));
        
        // Whitelist filter: only allow user and admin tools
        service.RegisterFilter(new ToolNamePatternFilter(new[] { "user_*", "admin_*" }, allowMatching: true, priority: 2));
        
        // Role filter: require admin role for admin tools (but admin tools already blocked above)
        var roleFilter = RoleBasedToolFilterBuilder.Create()
            .RequireRole("admin")
            .ForToolsMatching("admin_*")
            .Build();
        service.RegisterFilter(roleFilter);

        var tools = new[]
        {
            CreateTestTool("user_profile"),     // Should pass (allowed by whitelist, not blocked by blacklist)
            CreateTestTool("admin_delete"),     // Should be blocked (blocked by blacklist)
            CreateTestTool("system_status"),    // Should be blocked (not in whitelist)
            CreateTestTool("user_settings")     // Should pass (allowed by whitelist, not blocked by blacklist)
        };

        var context = CreateTestContext();

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        var toolNames = filteredTools.Select(t => t.Name).ToList();
        
        Assert.Equal(2, toolNames.Count);
        Assert.Contains("user_profile", toolNames);
        Assert.Contains("user_settings", toolNames);
        Assert.DoesNotContain("admin_delete", toolNames);
        Assert.DoesNotContain("system_status", toolNames);
    }

    [Fact]
    public async Task FilterChain_WithConditionalLogic_HandlesComplexScenarios()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        // Add filters that have conditional logic based on context
        service.RegisterFilter(new TimeBasedFilter(priority: 1));
        service.RegisterFilter(new UserLevelFilter(priority: 2));
        service.RegisterFilter(new ToolComplexityFilter(priority: 3));

        var tools = new[]
        {
            CreateTestTool("simple_read"),
            CreateTestTool("complex_analysis"),
            CreateTestTool("admin_operation"),
            CreateTestTool("time_sensitive_task")
        };

        var context = CreateTestContext();
        context.Properties["UserLevel"] = "premium";
        context.Properties["CurrentHour"] = DateTime.Now.Hour;

        // Act
        var filteredTools = await service.FilterToolsAsync(tools, context);

        // Assert
        var toolNames = filteredTools.Select(t => t.Name).ToList();
        
        // Specific assertions based on the filter logic
        Assert.Contains("simple_read", toolNames); // Should always be allowed
        
        // Other tools depend on time, user level, and complexity rules
        // The exact results depend on current time and context
    }

    [Fact]
    public async Task ToolExecution_WithMultipleFilters_RespectsAuthorizationChain()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        service.RegisterFilter(new AuthenticationFilter(priority: 1));
        service.RegisterFilter(new AuthorizationFilter(priority: 2));
        service.RegisterFilter(new QuotaFilter(priority: 3));

        var context = CreateTestContext();

        // Test 1: Unauthenticated user
        var result1 = await service.AuthorizeToolExecutionAsync("secure_api", context);
        Assert.False(result1.IsAuthorized);
        Assert.IsType<AuthorizationChallenge>(result1.AdditionalData);

        // Test 2: Authenticated but unauthorized user
        context.Properties["IsAuthenticated"] = true;
        var result2 = await service.AuthorizeToolExecutionAsync("admin_tool", context);
        Assert.False(result2.IsAuthorized);
        Assert.Contains("insufficient permissions", result2.Reason.ToLowerInvariant());

        // Test 3: Authorized user but quota exceeded
        context.Properties["HasAdminPermission"] = true;
        context.Properties["QuotaExceeded"] = true;
        var result3 = await service.AuthorizeToolExecutionAsync("admin_tool", context);
        Assert.False(result3.IsAuthorized);
        Assert.Contains("quota", result3.Reason.ToLowerInvariant());

        // Test 4: Fully authorized user with quota available
        context.Properties["QuotaExceeded"] = false;
        var result4 = await service.AuthorizeToolExecutionAsync("admin_tool", context);
        Assert.True(result4.IsAuthorized);
    }

    [Fact]
    public async Task FilterChain_WithDynamicFilters_HandlesRuntimeChanges()
    {
        // Arrange
        var service = new ToolAuthorizationService(LoggerFactory.CreateLogger<ToolAuthorizationService>());
        
        var dynamicFilter = new DynamicRulesFilter();
        service.RegisterFilter(dynamicFilter);

        var tools = new[] { CreateTestTool("dynamic_tool") };
        var context = CreateTestContext();

        // Act & Assert 1: Initially restrictive
        dynamicFilter.SetMode(DynamicRulesFilter.FilterMode.Restrictive);
        var result1 = await service.FilterToolsAsync(tools, context);
        Assert.Empty(result1);

        // Act & Assert 2: Change to permissive
        dynamicFilter.SetMode(DynamicRulesFilter.FilterMode.Permissive);
        var result2 = await service.FilterToolsAsync(tools, context);
        Assert.Single(result2);

        // Act & Assert 3: Change to selective
        dynamicFilter.SetMode(DynamicRulesFilter.FilterMode.Selective);
        dynamicFilter.AddAllowedTool("dynamic_tool");
        var result3 = await service.FilterToolsAsync(tools, context);
        Assert.Single(result3);

        // Act & Assert 4: Remove from allowed list
        dynamicFilter.RemoveAllowedTool("dynamic_tool");
        var result4 = await service.FilterToolsAsync(tools, context);
        Assert.Empty(result4);
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

    private static ToolAuthorizationContext CreateTestContext(string? sessionId = null, string? userId = null)
    {
        var context = ToolAuthorizationContext.ForSession(sessionId ?? "test-session");
        if (userId != null)
        {
            context.UserId = userId;
        }
        return context;
    }

    // Test filter implementations for complex scenarios

    private class SecurityFilter : IToolFilter
    {
        public static int CallOrder { get; private set; }
        private static int _globalCallOrder = 0;

        public SecurityFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            CallOrder = Interlocked.Increment(ref _globalCallOrder);
            
            // Block tools with "secure" in the name
            return Task.FromResult(!tool.Name.Contains("secure", StringComparison.OrdinalIgnoreCase));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("secure", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthorizationResult.Deny("Security policy violation"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class RateLimitFilter : IToolFilter
    {
        public static int CallOrder { get; private set; }
        private static int _globalCallOrder = 0;

        public RateLimitFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            CallOrder = Interlocked.Increment(ref _globalCallOrder);
            
            // Block high rate tools
            return Task.FromResult(!tool.Name.Contains("high_rate", StringComparison.OrdinalIgnoreCase));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("high_rate", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthorizationResult.Deny("Rate limit exceeded"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class RoleBasedFilter : IToolFilter
    {
        public static int CallOrder { get; private set; }
        private static int _globalCallOrder = 0;

        public RoleBasedFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            CallOrder = Interlocked.Increment(ref _globalCallOrder);
            
            // Block admin tools if not admin
            if (tool.Name.Contains("admin", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(context.UserRoles.Contains("admin"));
            }
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("admin", StringComparison.OrdinalIgnoreCase) && !context.UserRoles.Contains("admin"))
            {
                return Task.FromResult(AuthorizationResult.Deny("Insufficient role"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class FeatureFlagFilter : IToolFilter
    {
        public static int CallOrder { get; private set; }
        private static int _globalCallOrder = 0;

        public FeatureFlagFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            CallOrder = Interlocked.Increment(ref _globalCallOrder);
            
            // Block beta features
            return Task.FromResult(!tool.Name.Contains("beta", StringComparison.OrdinalIgnoreCase));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("beta", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthorizationResult.Deny("Feature not enabled"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class AuditFilter : IToolFilter
    {
        public static int CallOrder { get; private set; }
        private static int _globalCallOrder = 0;

        public AuditFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            CallOrder = Interlocked.Increment(ref _globalCallOrder);
            
            // Audit filter always allows (just logs)
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Log the access attempt (in real implementation)
            return Task.FromResult(AuthorizationResult.Allow("Audited"));
        }
    }

    // Additional test filter implementations...

    private class ExecutionTrackingFilter : IToolFilter
    {
        private readonly string _name;
        private readonly bool _shouldInclude;

        public ExecutionTrackingFilter(string name, int priority, bool shouldInclude)
        {
            _name = name;
            Priority = priority;
            _shouldInclude = shouldInclude;
        }

        public int Priority { get; }
        public bool WasCalled { get; private set; }
        public DateTime CallTime { get; private set; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            CallTime = DateTime.UtcNow;
            return Task.FromResult(_shouldInclude);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            CallTime = DateTime.UtcNow;
            return Task.FromResult(_shouldInclude 
                ? AuthorizationResult.Allow($"Allowed by {_name}") 
                : AuthorizationResult.Deny($"Denied by {_name}"));
        }
    }

    private class ExceptionThrowingFilter : IToolFilter
    {
        public ExceptionThrowingFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception in filter");
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception in filter");
        }
    }

    // Additional complex filter implementations for advanced scenarios...

    private class TimeBasedFilter : IToolFilter
    {
        public TimeBasedFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Block time-sensitive tasks outside business hours
            if (tool.Name.Contains("time_sensitive"))
            {
                var hour = DateTime.Now.Hour;
                return Task.FromResult(hour >= 9 && hour <= 17);
            }
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("time_sensitive"))
            {
                var hour = DateTime.Now.Hour;
                if (hour < 9 || hour > 17)
                {
                    return Task.FromResult(AuthorizationResult.Deny("Outside business hours"));
                }
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class UserLevelFilter : IToolFilter
    {
        public UserLevelFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Complex tools require premium users
            if (tool.Name.Contains("complex"))
            {
                return Task.FromResult(context.Properties.TryGetValue("UserLevel", out var level) && 
                                     level?.ToString() == "premium");
            }
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("complex"))
            {
                if (!context.Properties.TryGetValue("UserLevel", out var level) || level?.ToString() != "premium")
                {
                    return Task.FromResult(AuthorizationResult.Deny("Premium subscription required"));
                }
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class ToolComplexityFilter : IToolFilter
    {
        public ToolComplexityFilter(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Admin operations require special handling
            return Task.FromResult(!tool.Name.Contains("admin") || 
                                 context.Properties.ContainsKey("HasAdminPermission"));
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            if (toolName.Contains("admin") && !context.Properties.ContainsKey("HasAdminPermission"))
            {
                return Task.FromResult(AuthorizationResult.Deny("Admin permission required"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    // Filters for authorization chain testing

    private class AuthenticationFilter : IToolFilter
    {
        public AuthenticationFilter(int priority)
        {
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
            if (!context.Properties.TryGetValue("IsAuthenticated", out var auth) || !(bool)auth!)
            {
                return Task.FromResult(AuthorizationResult.DenyInvalidToken("mcp-server"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class AuthorizationFilter : IToolFilter
    {
        public AuthorizationFilter(int priority)
        {
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
            if (toolName.Contains("admin") && 
                (!context.Properties.TryGetValue("HasAdminPermission", out var perm) || !(bool)perm!))
            {
                return Task.FromResult(AuthorizationResult.Deny("Insufficient permissions"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class QuotaFilter : IToolFilter
    {
        public QuotaFilter(int priority)
        {
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
            if (context.Properties.TryGetValue("QuotaExceeded", out var exceeded) && (bool)exceeded!)
            {
                return Task.FromResult(AuthorizationResult.Deny("Quota exceeded"));
            }
            return Task.FromResult(AuthorizationResult.Allow());
        }
    }

    private class DynamicRulesFilter : IToolFilter
    {
        public enum FilterMode
        {
            Restrictive,
            Permissive,
            Selective
        }

        private FilterMode _mode = FilterMode.Restrictive;
        private readonly HashSet<string> _allowedTools = new();

        public int Priority => 100;

        public void SetMode(FilterMode mode)
        {
            _mode = mode;
        }

        public void AddAllowedTool(string toolName)
        {
            _allowedTools.Add(toolName);
        }

        public void RemoveAllowedTool(string toolName)
        {
            _allowedTools.Remove(toolName);
        }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            return _mode switch
            {
                FilterMode.Restrictive => Task.FromResult(false),
                FilterMode.Permissive => Task.FromResult(true),
                FilterMode.Selective => Task.FromResult(_allowedTools.Contains(tool.Name)),
                _ => Task.FromResult(false)
            };
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var allowed = _mode switch
            {
                FilterMode.Restrictive => false,
                FilterMode.Permissive => true,
                FilterMode.Selective => _allowedTools.Contains(toolName),
                _ => false
            };

            return Task.FromResult(allowed 
                ? AuthorizationResult.Allow($"Dynamic rule: {_mode}") 
                : AuthorizationResult.Deny($"Dynamic rule: {_mode}"));
        }
    }
}