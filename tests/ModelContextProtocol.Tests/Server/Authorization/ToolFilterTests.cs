using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server.Authorization;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Unit tests for IToolFilter implementations.
/// </summary>
public class ToolFilterTests
{
    [Fact]
    public async Task AllowAllToolFilter_ShouldIncludeToolAsync_ReturnsTrue()
    {
        // Arrange
        var filter = new AllowAllToolFilter();
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await filter.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AllowAllToolFilter_CanExecuteToolAsync_ReturnsAllow()
    {
        // Arrange
        var filter = new AllowAllToolFilter();
        var context = CreateTestContext();

        // Act
        var result = await filter.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("All tools allowed", result.Reason);
    }

    [Fact]
    public void AllowAllToolFilter_Priority_DefaultsToMaxValue()
    {
        // Arrange & Act
        var filter = new AllowAllToolFilter();

        // Assert
        Assert.Equal(int.MaxValue, filter.Priority);
    }

    [Fact]
    public void AllowAllToolFilter_Priority_CanBeSet()
    {
        // Arrange
        const int expectedPriority = 100;

        // Act
        var filter = new AllowAllToolFilter(expectedPriority);

        // Assert
        Assert.Equal(expectedPriority, filter.Priority);
    }

    [Fact]
    public async Task DenyAllToolFilter_ShouldIncludeToolAsync_ReturnsFalse()
    {
        // Arrange
        var filter = new DenyAllToolFilter();
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act
        var result = await filter.ShouldIncludeToolAsync(tool, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task DenyAllToolFilter_CanExecuteToolAsync_ReturnsDeny()
    {
        // Arrange
        var filter = new DenyAllToolFilter();
        var context = CreateTestContext();

        // Act
        var result = await filter.CanExecuteToolAsync("test_tool", context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Equal("All tools denied", result.Reason);
    }

    [Fact]
    public void DenyAllToolFilter_Priority_DefaultsToZero()
    {
        // Arrange & Act
        var filter = new DenyAllToolFilter();

        // Assert
        Assert.Equal(0, filter.Priority);
    }

    [Fact]
    public void DenyAllToolFilter_Priority_CanBeSet()
    {
        // Arrange
        const int expectedPriority = 50;

        // Act
        var filter = new DenyAllToolFilter(expectedPriority);

        // Assert
        Assert.Equal(expectedPriority, filter.Priority);
    }

    [Theory]
    [InlineData("admin_delete_tool")]
    [InlineData("admin_modify_user")]
    [InlineData("private_data_access")]
    [InlineData("delete_all_files")]
    public async Task OAuthToolFilter_HighPrivilegeTools_RequiresScope(string toolName)
    {
        // Arrange
        var filter = new TestOAuthToolFilter("write:admin", "test-realm");
        var tool = CreateTestTool(toolName);
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync(toolName, context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
        Assert.Contains("Insufficient scope", executeResult.Reason);
        Assert.IsType<AuthorizationChallenge>(executeResult.AdditionalData);

        var challenge = (AuthorizationChallenge)executeResult.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("scope=\"write:admin\"", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"insufficient_scope\"", challenge.WwwAuthenticateValue);
        Assert.Contains("realm=\"test-realm\"", challenge.WwwAuthenticateValue);
    }

    [Theory]
    [InlineData("secure_tool")]
    [InlineData("user_data_tool")]
    [InlineData("private_tool")]
    public async Task OAuthToolFilter_SecureTools_RequiresValidToken(string toolName)
    {
        // Arrange
        var filter = new TestOAuthToolFilter("read:basic", "test-realm");
        var tool = CreateTestTool(toolName);
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync(toolName, context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
        Assert.Contains("Invalid or expired token", executeResult.Reason);
        Assert.IsType<AuthorizationChallenge>(executeResult.AdditionalData);

        var challenge = (AuthorizationChallenge)executeResult.AdditionalData;
        Assert.Contains("Bearer", challenge.WwwAuthenticateValue);
        Assert.Contains("error=\"invalid_token\"", challenge.WwwAuthenticateValue);
        Assert.Contains("realm=\"test-realm\"", challenge.WwwAuthenticateValue);
    }

    [Theory]
    [InlineData("public_info_tool")]
    [InlineData("read_only_tool")]
    [InlineData("public_read_tool")]
    public async Task OAuthToolFilter_PublicTools_AllowsAccess(string toolName)
    {
        // Arrange
        var filter = new TestOAuthToolFilter("read:basic", "test-realm");
        var tool = CreateTestTool(toolName);
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync(toolName, context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
        Assert.Equal("Valid credentials", executeResult.Reason);
    }

    [Fact]
    public async Task ToolNamePatternFilter_MatchingPattern_AllowsAccess()
    {
        // Arrange
        var filter = new ToolNamePatternFilter(new[] { "read_*", "get_*" }, allowMatching: true);
        var tool = CreateTestTool("read_data");
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("get_info", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task ToolNamePatternFilter_NonMatchingPattern_DeniesAccess()
    {
        // Arrange
        var filter = new ToolNamePatternFilter(new[] { "read_*", "get_*" }, allowMatching: true);
        var tool = CreateTestTool("delete_data");
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("delete_data", context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task ToolNamePatternFilter_BlockPattern_DeniesMatchingTools()
    {
        // Arrange
        var filter = new ToolNamePatternFilter(new[] { "delete_*", "remove_*" }, allowMatching: false);
        var tool = CreateTestTool("delete_file");
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("remove_user", context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task ToolNamePatternFilter_BlockPattern_AllowsNonMatchingTools()
    {
        // Arrange
        var filter = new ToolNamePatternFilter(new[] { "delete_*", "remove_*" }, allowMatching: false);
        var tool = CreateTestTool("read_file");
        var context = CreateTestContext();

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("create_user", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task RoleBasedToolFilter_UserWithRequiredRole_AllowsAccess()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireRole("admin")
            .ForToolsMatching("admin_*")
            .Build();

        var tool = CreateTestTool("admin_panel");
        var context = CreateTestContext();
        context.UserRoles.Add("admin");

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("admin_delete", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task RoleBasedToolFilter_UserWithoutRequiredRole_DeniesAccess()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireRole("admin")
            .ForToolsMatching("admin_*")
            .Build();

        var tool = CreateTestTool("admin_panel");
        var context = CreateTestContext();
        context.UserRoles.Add("user");

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("admin_delete", context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
        Assert.Contains("Required role", executeResult.Reason);
    }

    [Fact]
    public async Task RoleBasedToolFilter_NonMatchingTool_AllowsAccess()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireRole("admin")
            .ForToolsMatching("admin_*")
            .Build();

        var tool = CreateTestTool("user_profile");
        var context = CreateTestContext();
        context.UserRoles.Add("user");

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("user_profile", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task RoleBasedToolFilter_MultipleRoles_AllowsAccessWithAnyRole()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireAnyRole("admin", "moderator")
            .ForToolsMatching("*_manage")
            .Build();

        var tool = CreateTestTool("user_manage");
        var context = CreateTestContext();
        context.UserRoles.Add("moderator");

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("content_manage", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task RoleBasedToolFilter_AllRolesRequired_RequiresAllRoles()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireAllRoles("admin", "security")
            .ForToolsMatching("security_*")
            .Build();

        var tool = CreateTestTool("security_audit");
        var context = CreateTestContext();
        context.UserRoles.Add("admin");
        // Missing "security" role

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("security_audit", context);

        // Assert
        Assert.False(includeResult);
        Assert.False(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task RoleBasedToolFilter_AllRolesRequired_AllowsWithAllRoles()
    {
        // Arrange
        var filter = RoleBasedToolFilterBuilder.Create()
            .RequireAllRoles("admin", "security")
            .ForToolsMatching("security_*")
            .Build();

        var tool = CreateTestTool("security_audit");
        var context = CreateTestContext();
        context.UserRoles.Add("admin");
        context.UserRoles.Add("security");

        // Act
        var includeResult = await filter.ShouldIncludeToolAsync(tool, context);
        var executeResult = await filter.CanExecuteToolAsync("security_audit", context);

        // Assert
        Assert.True(includeResult);
        Assert.True(executeResult.IsAuthorized);
    }

    [Fact]
    public async Task CustomToolFilter_WithException_DeniesAccess()
    {
        // Arrange
        var filter = new ExceptionThrowingFilter();
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            filter.ShouldIncludeToolAsync(tool, context));
        
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            filter.CanExecuteToolAsync("test_tool", context));
    }

    [Fact]
    public async Task AsyncToolFilter_HandlesCancellation()
    {
        // Arrange
        var filter = new SlowFilter();
        var tool = CreateTestTool("test_tool");
        var context = CreateTestContext();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            filter.ShouldIncludeToolAsync(tool, context, cts.Token));
        
        await Assert.ThrowsAsync<OperationCanceledException>(() => 
            filter.CanExecuteToolAsync("test_tool", context, cts.Token));
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
            throw new InvalidOperationException("Test exception");
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Test exception");
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

    /// <summary>
    /// Test implementation of OAuth tool filter demonstrating proper authorization challenge handling.
    /// </summary>
    private class TestOAuthToolFilter : IToolFilter
    {
        private readonly string _requiredScope;
        private readonly string? _realm;

        public TestOAuthToolFilter(string requiredScope, string? realm = null)
        {
            _requiredScope = requiredScope ?? throw new ArgumentNullException(nameof(requiredScope));
            _realm = realm;
        }

        public int Priority => 100;

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var result = await CanExecuteToolAsync(tool.Name, context, cancellationToken);
            return result.IsAuthorized;
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Simulate checking if the user has the required scope
            if (IsHighPrivilegeTool(toolName))
            {
                // For high-privilege tools, require specific scope
                if (!HasRequiredScope(context))
                {
                    return Task.FromResult(AuthorizationResult.DenyInsufficientScope(_requiredScope, _realm));
                }
            }
            else if (RequiresAuthentication(toolName))
            {
                // For tools that require authentication, check for valid token
                if (!HasValidToken(context))
                {
                    return Task.FromResult(AuthorizationResult.DenyInvalidToken(_realm));
                }
            }

            // Tool is authorized
            return Task.FromResult(AuthorizationResult.Allow("Valid credentials"));
        }

        private static bool IsHighPrivilegeTool(string toolName)
        {
            return toolName.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("private", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresAuthentication(string toolName)
        {
            return !toolName.Contains("public", StringComparison.OrdinalIgnoreCase) &&
                   !toolName.Contains("read", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasRequiredScope(ToolAuthorizationContext context)
        {
            // For testing, always deny high-privilege tools to demonstrate challenge
            return false;
        }

        private bool HasValidToken(ToolAuthorizationContext context)
        {
            // For testing, always deny to demonstrate invalid token challenge
            return false;
        }
    }
}