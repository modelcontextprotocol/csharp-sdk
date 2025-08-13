using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using Moq;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Unit tests for ToolAuthorizationService functionality.
/// </summary>
public class ToolAuthorizationServiceTests
{
    [Fact]
    public void Constructor_WithNullFilters_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ToolAuthorizationService((IEnumerable<IToolFilter>)null!));
    }

    [Fact]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        // Arrange
        var logger = Mock.Of<ILogger<ToolAuthorizationService>>();

        // Act & Assert
        var service = new ToolAuthorizationService(logger);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithFiltersAndLogger_RegistersFilters()
    {
        // Arrange
        var filter1 = new AllowAllToolFilter(1);
        var filter2 = new DenyAllToolFilter(2);
        var filters = new IToolFilter[] { filter1, filter2 };
        var logger = Mock.Of<ILogger<ToolAuthorizationService>>();

        // Act
        var service = new ToolAuthorizationService(filters, logger);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Equal(2, registeredFilters.Count);
        Assert.Contains(filter1, registeredFilters);
        Assert.Contains(filter2, registeredFilters);
    }

    [Fact]
    public void Constructor_WithFiltersContainingNull_IgnoresNullFilters()
    {
        // Arrange
        var filter1 = new AllowAllToolFilter();
        var filters = new IToolFilter?[] { filter1, null, filter1 };

        // Act
        var service = new ToolAuthorizationService(filters.Where(f => f != null)!);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Equal(2, registeredFilters.Count);
        Assert.All(registeredFilters, f => Assert.Equal(filter1, f));
    }

    [Fact]
    public async Task FilterToolsAsync_WithNullTools_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.FilterToolsAsync(null!, context));
    }

    [Fact]
    public async Task FilterToolsAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var tools = new[] { CreateTestTool("test") };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.FilterToolsAsync(tools, null!));
    }

    [Fact]
    public async Task FilterToolsAsync_WithNoFilters_ReturnsAllTools()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var tools = new[] { CreateTestTool("tool1"), CreateTestTool("tool2") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Equal(tools, result);
    }

    [Fact]
    public async Task FilterToolsAsync_WithAllowAllFilter_ReturnsAllTools()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new AllowAllToolFilter());
        var tools = new[] { CreateTestTool("tool1"), CreateTestTool("tool2") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Equal(tools, result);
    }

    [Fact]
    public async Task FilterToolsAsync_WithDenyAllFilter_ReturnsNoTools()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new DenyAllToolFilter());
        var tools = new[] { CreateTestTool("tool1"), CreateTestTool("tool2") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task FilterToolsAsync_WithSelectiveFilter_ReturnsFilteredTools()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new ToolNamePatternFilter(new[] { "read_*" }, allowMatching: true));
        var tools = new[] 
        { 
            CreateTestTool("read_data"), 
            CreateTestTool("write_data"), 
            CreateTestTool("read_file") 
        };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, t => t.Name == "read_data");
        Assert.Contains(result, t => t.Name == "read_file");
        Assert.DoesNotContain(result, t => t.Name == "write_data");
    }

    [Fact]
    public async Task FilterToolsAsync_WithMultipleFilters_AppliesPriorityOrder()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        // Lower priority number = higher priority
        service.RegisterFilter(new AllowAllToolFilter(priority: 100)); // Lower priority
        service.RegisterFilter(new DenyAllToolFilter(priority: 1));    // Higher priority
        var tools = new[] { CreateTestTool("tool1") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        // DenyAllToolFilter should execute first and block all tools
        Assert.Empty(result);
    }

    [Fact]
    public async Task FilterToolsAsync_WithMultipleFilters_StopsOnFirstDeny()
    {
        // Arrange
        var mockFilter1 = new Mock<IToolFilter>();
        var mockFilter2 = new Mock<IToolFilter>();
        
        mockFilter1.Setup(f => f.Priority).Returns(1);
        mockFilter1.Setup(f => f.ShouldIncludeToolAsync(It.IsAny<Tool>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);
        
        mockFilter2.Setup(f => f.Priority).Returns(2);
        
        var service = new ToolAuthorizationService();
        service.RegisterFilter(mockFilter1.Object);
        service.RegisterFilter(mockFilter2.Object);
        
        var tools = new[] { CreateTestTool("tool1") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Empty(result);
        mockFilter1.Verify(f => f.ShouldIncludeToolAsync(It.IsAny<Tool>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Once);
        // Second filter should not be called since first filter denied
        mockFilter2.Verify(f => f.ShouldIncludeToolAsync(It.IsAny<Tool>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FilterToolsAsync_WithFilterException_DeniesAccess()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new ExceptionThrowingFilter());
        var tools = new[] { CreateTestTool("tool1") };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithNullOrEmptyToolName_ThrowsArgumentException()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            service.AuthorizeToolExecutionAsync(null!, context));
        await Assert.ThrowsAsync<ArgumentException>(() => 
            service.AuthorizeToolExecutionAsync("", context));
        await Assert.ThrowsAsync<ArgumentException>(() => 
            service.AuthorizeToolExecutionAsync("   ", context));
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new ToolAuthorizationService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            service.AuthorizeToolExecutionAsync("test_tool", null!));
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithNoFilters_ReturnsAllow()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var context = CreateTestContext();

        // Act
        var result = await service.AuthorizeToolExecutionAsync("test_tool", context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("No filters configured", result.Reason);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithAllowFilter_ReturnsAllow()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new AllowAllToolFilter());
        var context = CreateTestContext();

        // Act
        var result = await service.AuthorizeToolExecutionAsync("test_tool", context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("All filters passed", result.Reason);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithDenyFilter_ReturnsDeny()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new DenyAllToolFilter());
        var context = CreateTestContext();

        // Act
        var result = await service.AuthorizeToolExecutionAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("All tools denied", result.Reason);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithMultipleFilters_StopsOnFirstDeny()
    {
        // Arrange
        var mockFilter1 = new Mock<IToolFilter>();
        var mockFilter2 = new Mock<IToolFilter>();
        
        mockFilter1.Setup(f => f.Priority).Returns(1);
        mockFilter1.Setup(f => f.CanExecuteToolAsync(It.IsAny<string>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(AuthorizationResult.Deny("Access denied"));
        
        mockFilter2.Setup(f => f.Priority).Returns(2);
        
        var service = new ToolAuthorizationService();
        service.RegisterFilter(mockFilter1.Object);
        service.RegisterFilter(mockFilter2.Object);
        
        var context = CreateTestContext();

        // Act
        var result = await service.AuthorizeToolExecutionAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("Access denied", result.Reason);
        mockFilter1.Verify(f => f.CanExecuteToolAsync("test_tool", context, It.IsAny<CancellationToken>()), Times.Once);
        // Second filter should not be called since first filter denied
        mockFilter2.Verify(f => f.CanExecuteToolAsync(It.IsAny<string>(), It.IsAny<ToolAuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithFilterException_ReturnsDeny()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new ExceptionThrowingFilter());
        var context = CreateTestContext();

        // Act
        var result = await service.AuthorizeToolExecutionAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Filter error", result.Reason);
    }

    [Fact]
    public async Task AuthorizeToolExecutionAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        service.RegisterFilter(new SlowFilter());
        var context = CreateTestContext();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            service.AuthorizeToolExecutionAsync("test_tool", context, cts.Token));
    }

    [Fact]
    public void RegisterFilter_WithNullFilter_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new ToolAuthorizationService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.RegisterFilter(null!));
    }

    [Fact]
    public void RegisterFilter_WithValidFilter_AddsToCollection()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter = new AllowAllToolFilter();

        // Act
        service.RegisterFilter(filter);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Single(registeredFilters);
        Assert.Contains(filter, registeredFilters);
    }

    [Fact]
    public void RegisterFilter_WithMultipleFilters_AddsAll()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter1 = new AllowAllToolFilter();
        var filter2 = new DenyAllToolFilter();

        // Act
        service.RegisterFilter(filter1);
        service.RegisterFilter(filter2);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Equal(2, registeredFilters.Count);
        Assert.Contains(filter1, registeredFilters);
        Assert.Contains(filter2, registeredFilters);
    }

    [Fact]
    public void UnregisterFilter_WithNullFilter_ThrowsArgumentNullException()
    {
        // Arrange
        var service = new ToolAuthorizationService();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.UnregisterFilter(null!));
    }

    [Fact]
    public void UnregisterFilter_WithRegisteredFilter_RemovesFromCollection()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter = new AllowAllToolFilter();
        service.RegisterFilter(filter);

        // Act
        service.UnregisterFilter(filter);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Empty(registeredFilters);
    }

    [Fact]
    public void UnregisterFilter_WithUnregisteredFilter_DoesNotThrow()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter = new AllowAllToolFilter();

        // Act & Assert
        service.UnregisterFilter(filter); // Should not throw
        Assert.Empty(service.GetRegisteredFilters());
    }

    [Fact]
    public void UnregisterFilter_WithMultipleFilters_RemovesOnlySpecified()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter1 = new AllowAllToolFilter();
        var filter2 = new DenyAllToolFilter();
        service.RegisterFilter(filter1);
        service.RegisterFilter(filter2);

        // Act
        service.UnregisterFilter(filter1);

        // Assert
        var registeredFilters = service.GetRegisteredFilters();
        Assert.Single(registeredFilters);
        Assert.Contains(filter2, registeredFilters);
        Assert.DoesNotContain(filter1, registeredFilters);
    }

    [Fact]
    public void GetRegisteredFilters_ReturnsReadOnlyCollection()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var filter = new AllowAllToolFilter();
        service.RegisterFilter(filter);

        // Act
        var registeredFilters = service.GetRegisteredFilters();

        // Assert
        Assert.IsAssignableFrom<IReadOnlyCollection<IToolFilter>>(registeredFilters);
        Assert.Single(registeredFilters);
        Assert.Contains(filter, registeredFilters);
    }

    [Fact]
    public async Task ConcurrentOperations_AreThreadSafe()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        var tools = Enumerable.Range(0, 100).Select(i => CreateTestTool($"tool{i}")).ToArray();
        var context = CreateTestContext();

        // Act - Run multiple concurrent operations
        var tasks = new List<Task>();
        
        // Register filters concurrently
        for (int i = 0; i < 10; i++)
        {
            var filter = new AllowAllToolFilter(i);
            tasks.Add(Task.Run(() => service.RegisterFilter(filter)));
        }

        // Filter tools concurrently
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(async () => await service.FilterToolsAsync(tools, context)));
        }

        // Authorize tools concurrently
        for (int i = 0; i < 10; i++)
        {
            var toolName = $"tool{i}";
            tasks.Add(Task.Run(async () => await service.AuthorizeToolExecutionAsync(toolName, context)));
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions should be thrown and operations should complete
        Assert.Equal(10, service.GetRegisteredFilters().Count);
    }

    [Fact]
    public async Task FilterToolsAsync_WithMixedFilters_AppliesCorrectLogic()
    {
        // Arrange
        var service = new ToolAuthorizationService();
        
        // Add filters with different priorities and behaviors
        service.RegisterFilter(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1)); // Block admin tools first
        service.RegisterFilter(new AllowAllToolFilter(priority: 100)); // Allow everything else
        
        var tools = new[] 
        { 
            CreateTestTool("admin_delete"), 
            CreateTestTool("user_profile"), 
            CreateTestTool("admin_create"),
            CreateTestTool("public_info")
        };
        var context = CreateTestContext();

        // Act
        var result = await service.FilterToolsAsync(tools, context);

        // Assert
        Assert.Equal(2, result.Count());
        Assert.Contains(result, t => t.Name == "user_profile");
        Assert.Contains(result, t => t.Name == "public_info");
        Assert.DoesNotContain(result, t => t.Name == "admin_delete");
        Assert.DoesNotContain(result, t => t.Name == "admin_create");
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
    /// Test filter that simulates slow operations for testing cancellation.
    /// </summary>
    private class SlowFilter : IToolFilter
    {
        public int Priority => 1;

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5000, cancellationToken);
            return true;
        }

        public async Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(5000, cancellationToken);
            return AuthorizationResult.Allow();
        }
    }
}