using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Unit tests for ToolFilterAggregator and filter priority handling.
/// </summary>
public class ToolFilterAggregatorTests
{
    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ToolFilterAggregator(null!));
    }

    [Fact]
    public void Constructor_WithValidServiceProvider_DoesNotThrow()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var logger = Mock.Of<ILogger<ToolFilterAggregator>>();

        // Act & Assert
        var aggregator = new ToolFilterAggregator(serviceProvider, logger);
        Assert.NotNull(aggregator);
    }

    [Fact]
    public void Priority_ReturnsMinValue()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var aggregator = new ToolFilterAggregator(serviceProvider);

        // Act & Assert
        Assert.Equal(int.MinValue, aggregator.Priority);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithNullTool_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var aggregator = new ToolFilterAggregator(serviceProvider);
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            aggregator.ShouldIncludeToolAsync(null!, context));
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var aggregator = new ToolFilterAggregator(serviceProvider);
        var tool = CreateTestTool("test");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            aggregator.ShouldIncludeToolAsync(tool, null!));
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithNoFilters_ReturnsTrue()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(Array.Empty<IToolFilter>());

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithAllowAllFilter_ReturnsTrue()
    {
        // Arrange
        var filters = new IToolFilter[] { new AllowAllToolFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithDenyAllFilter_ReturnsFalse()
    {
        // Arrange
        var filters = new IToolFilter[] { new DenyAllToolFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithMultipleFilters_AppliesPriorityOrder()
    {
        // Arrange
        var filter1 = new AllowAllToolFilter(priority: 100); // Lower priority
        var filter2 = new DenyAllToolFilter(priority: 1);    // Higher priority (should execute first)
        var filters = new IToolFilter[] { filter1, filter2 };
        
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        // DenyAllToolFilter should execute first due to higher priority and block the tool
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_WithFilterException_ReturnsFalse()
    {
        // Arrange
        var filters = new IToolFilter[] { new ExceptionThrowingFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldIncludeToolAsync_ExcludesSelfFromFilters()
    {
        // Arrange
        var otherFilter = new AllowAllToolFilter();
        var mockServiceProvider = new Mock<IServiceProvider>();
        
        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        
        // Setup service provider to return the aggregator itself along with other filters
        var filters = new IToolFilter[] { aggregator, otherFilter };
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        // Should not cause infinite recursion and should work with the other filter
        Assert.True(result);
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithNullOrEmptyToolName_ThrowsArgumentException()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var aggregator = new ToolFilterAggregator(serviceProvider);
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            aggregator.CanExecuteToolAsync(null!, context));
        await Assert.ThrowsAsync<ArgumentException>(() => 
            aggregator.CanExecuteToolAsync("", context));
        await Assert.ThrowsAsync<ArgumentException>(() => 
            aggregator.CanExecuteToolAsync("   ", context));
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = Mock.Of<IServiceProvider>();
        var aggregator = new ToolFilterAggregator(serviceProvider);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            aggregator.CanExecuteToolAsync("test_tool", null!));
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithNoFilters_ReturnsAllow()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(Array.Empty<IToolFilter>());

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var context = CreateTestContext();

        // Act
        var result = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("No filters configured", result.Reason);
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithAllowAllFilter_ReturnsAllow()
    {
        // Arrange
        var filters = new IToolFilter[] { new AllowAllToolFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var context = CreateTestContext();

        // Act
        var result = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("All aggregated filters passed", result.Reason);
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithDenyAllFilter_ReturnsDeny()
    {
        // Arrange
        var filters = new IToolFilter[] { new DenyAllToolFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var context = CreateTestContext();

        // Act
        var result = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("All tools denied", result.Reason);
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithMultipleFilters_StopsOnFirstDeny()
    {
        // Arrange
        var mockFilter1 = new Mock<IToolFilter>();
        var mockFilter2 = new Mock<IToolFilter>();
        
        mockFilter1.Setup(f => f.Priority).Returns(1);
        mockFilter1.Setup(f => f.CanExecuteToolAsync(It.IsAny<string>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(AuthorizationResult.Deny("Access denied"));
        
        mockFilter2.Setup(f => f.Priority).Returns(2);
        
        var filters = new IToolFilter[] { mockFilter1.Object, mockFilter2.Object };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var context = CreateTestContext();

        // Act
        var result = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Access denied", result.Reason);
        
        mockFilter1.Verify(f => f.CanExecuteToolAsync("test_tool", context, It.IsAny<CancellationToken>()), Times.Once);
        // Second filter should not be called since first filter denied
        mockFilter2.Verify(f => f.CanExecuteToolAsync(It.IsAny<string>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CanExecuteToolAsync_WithFilterException_ReturnsDeny()
    {
        // Arrange
        var filters = new IToolFilter[] { new ExceptionThrowingFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var context = CreateTestContext();

        // Act
        var result = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Filter error", result.Reason);
        Assert.Contains("ExceptionThrowingFilter", result.Reason);
    }

    [Fact]
    public async Task FilterOperations_WithServiceProviderException_HandleGracefully()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Throws(new InvalidOperationException("Service resolution failed"));

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var includeResult = await aggregator.ShouldIncludeToolAsync(tool, context);
        var executeResult = await aggregator.CanExecuteToolAsync("test_tool", context);

        // Assert
        // Should return safe defaults when service resolution fails
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
        Assert.Equal("No filters configured", executeResult.Reason);
    }

    [Fact]
    public void ClearCache_ClearsFilterCache()
    {
        // Arrange
        var filters = new IToolFilter[] { new AllowAllToolFilter() };
        var mockServiceProvider = new Mock<IServiceProvider>();
        
        // Setup to return different results on subsequent calls
        var setupSequence = mockServiceProvider.SetupSequence(sp => sp.GetServices<IToolFilter>());
        setupSequence.Returns(filters);
        setupSequence.Returns(Array.Empty<IToolFilter>());

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        
        // Act & Assert
        // First call should cache the filters
        _ = aggregator.ShouldIncludeToolAsync(CreateTestTool("test"), CreateTestContext());
        
        // Clear cache
        aggregator.ClearCache();
        
        // Second call should get new filters from service provider
        _ = aggregator.ShouldIncludeToolAsync(CreateTestTool("test"), CreateTestContext());
        
        // Verify service provider was called twice (once for initial, once after cache clear)
        mockServiceProvider.Verify(sp => sp.GetServices<IToolFilter>(), Times.Exactly(2));
    }

    [Fact]
    public async Task FilterAggregator_WithComplexPriorityScenario_AppliesCorrectOrder()
    {
        // Arrange
        var filters = new IToolFilter[]
        {
            new TestOrderedFilter("Filter1", priority: 10, allow: true),
            new TestOrderedFilter("Filter2", priority: 5, allow: true),  // Should execute first
            new TestOrderedFilter("Filter3", priority: 15, allow: false), // Should execute last but won't be reached
            new TestOrderedFilter("Filter4", priority: 7, allow: false)   // Should execute second and deny
        };
        
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetServices<IToolFilter>())
                          .Returns(filters);

        var aggregator = new ToolFilterAggregator(mockServiceProvider.Object);
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await aggregator.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.False(result); // Should be denied by Filter4
        
        // Verify execution order by checking which filters were called
        var filter2 = (TestOrderedFilter)filters[1]; // Priority 5
        var filter4 = (TestOrderedFilter)filters[3]; // Priority 7
        var filter1 = (TestOrderedFilter)filters[0]; // Priority 10
        var filter3 = (TestOrderedFilter)filters[2]; // Priority 15
        
        Assert.True(filter2.WasCalled); // Should be called first
        Assert.True(filter4.WasCalled); // Should be called second and deny
        Assert.False(filter1.WasCalled); // Should not be called due to earlier denial
        Assert.False(filter3.WasCalled); // Should not be called due to earlier denial
    }

    [Fact]
    public async Task FilterAggregator_WithRealWorldDIContainer_WorksCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IToolFilter>(new AllowAllToolFilter(priority: 100));
        services.AddSingleton<IToolFilter>(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1));
        services.AddSingleton<IToolFilter>(new DenyAllToolFilter(priority: 50));
        
        var serviceProvider = services.BuildServiceProvider();
        var aggregator = new ToolFilterAggregator(serviceProvider);
        
        var adminTool = CreateTestTool("admin_delete");
        var userTool = CreateTestTool("user_profile");
        var context = CreateTestContext();

        // Act
        var adminResult = await aggregator.ShouldIncludeToolAsync(adminTool, context);
        var userResult = await aggregator.ShouldIncludeToolAsync(userTool, context);

        // Assert
        // Admin tool should be blocked by pattern filter (priority 1)
        Assert.False(adminResult);
        
        // User tool should be blocked by deny all filter (priority 50, executes after pattern filter allows it)
        Assert.False(userResult);
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

    /// <summary>
    /// Test filter that throws exceptions to test error handling.
    /// </summary>
    private class ExceptionThrowingFilter : IToolFilter
    {
        public int Priority => 1;

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception in ShouldIncludeToolAsync");
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception in CanExecuteToolAsync");
        }
    }

    /// <summary>
    /// Test filter that tracks execution order and allows testing priority handling.
    /// </summary>
    private class TestOrderedFilter : IToolFilter
    {
        private readonly string _name;
        private readonly bool _allowResult;

        public TestOrderedFilter(string name, int priority, bool allow)
        {
            _name = name;
            Priority = priority;
            _allowResult = allow;
        }

        public int Priority { get; }
        public bool WasCalled { get; private set; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(_allowResult);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(_allowResult 
                ? AuthorizationResult.Allow($"Allowed by {_name}") 
                : AuthorizationResult.Deny($"Denied by {_name}"));
        }

        public override string ToString() => $"{_name} (Priority: {Priority})";
    }
}