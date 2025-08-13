using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Nodes;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Integration tests for end-to-end tool filtering in MCP server operations.
/// </summary>
public class ToolFilteringIntegrationTests : LoggedTest
{
    public ToolFilteringIntegrationTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task ListTools_WithNoFilters_ReturnsAllTools()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Tools.Count);
        Assert.Contains(result.Tools, t => t.Name == "read_tool");
        Assert.Contains(result.Tools, t => t.Name == "write_tool");
        Assert.Contains(result.Tools, t => t.Name == "admin_tool");
    }

    [Fact]
    public async Task ListTools_WithDenyAllFilter_ReturnsNoTools()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        // Add authorization service with deny all filter
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            authService.RegisterFilter(new DenyAllToolFilter());
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Tools);
    }

    [Fact]
    public async Task ListTools_WithPatternFilter_ReturnsFilteredTools()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        // Add authorization service with pattern filter
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            // Only allow read tools
            authService.RegisterFilter(new ToolNamePatternFilter(new[] { "read_*" }, allowMatching: true));
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Tools);
        Assert.Equal("read_tool", result.Tools[0].Name);
    }

    [Fact]
    public async Task ListTools_WithDecoratorPattern_FiltersAllToolsFromAllSources()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        
        // Add tools through different sources to test decorator pattern
        AddTestTools(serverOptions);
        
        // Add a custom list tools handler that adds additional tools (simulating another source)
        serverOptions.ListToolsHandler = async (request, cancellationToken) =>
        {
            return new ListToolsResult
            {
                Tools = { new Tool { Name = "external_tool", Description = "Tool from external source" } }
            };
        };
        
        // Add filter that blocks admin tools (should affect tools from all sources)
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            authService.RegisterFilter(new ToolNamePatternFilter(
                new[] { "^(?!admin_).*" }, // Allow all except admin_ tools
                Array.Empty<string>(),
                priority: 100));
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        // Should have tools from both sources, but admin tools should be filtered out
        Assert.Contains(result.Tools, t => t.Name == "read_tool"); // From AddTestTools
        Assert.Contains(result.Tools, t => t.Name == "external_tool"); // From custom handler
        Assert.DoesNotContain(result.Tools, t => t.Name.StartsWith("admin_")); // Filtered out
    }

    [Fact]
    public async Task ListTools_WithMultipleFilters_AppliesPriorityOrder()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        // Add authorization service with multiple filters
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            // Higher priority filter blocks admin tools
            authService.RegisterFilter(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1));
            // Lower priority filter would allow all, but won't affect admin tools
            authService.RegisterFilter(new AllowAllToolFilter(priority: 100));
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Tools.Count);
        Assert.Contains(result.Tools, t => t.Name == "read_tool");
        Assert.Contains(result.Tools, t => t.Name == "write_tool");
        Assert.DoesNotContain(result.Tools, t => t.Name == "admin_tool");
    }

    [Fact]
    public async Task CallTool_WithAllowAllFilter_Succeeds()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            authService.RegisterFilter(new AllowAllToolFilter());
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.CallToolAsync("read_tool", new { }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Content);
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextResourceContents>(result.Content[0]);
        Assert.Equal("Read operation completed", textContent.Text);
    }

    [Fact]
    public async Task CallTool_WithDenyAllFilter_ThrowsException()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            authService.RegisterFilter(new DenyAllToolFilter());
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync("read_tool", new { }, TestContext.Current.CancellationToken));
        
        Assert.Contains("denied", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task CallTool_WithRoleBasedFilter_RequiresCorrectRole()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            // Require admin role for admin tools
            var filter = RoleBasedToolFilterBuilder.Create()
                .RequireRole("admin")
                .ForToolsMatching("admin_*")
                .Build();
            authService.RegisterFilter(filter);
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act & Assert - Should fail without admin role
        var exception = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync("admin_tool", new { }, TestContext.Current.CancellationToken));
        
        Assert.Contains("role", exception.Message.ToLowerInvariant());

        // Should succeed for non-admin tools
        var result = await client.CallToolAsync("read_tool", new { }, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ToolFiltering_WithFilterException_HandlesGracefully()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService>(sp =>
        {
            var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
            authService.RegisterFilter(new ExceptionThrowingFilter());
            return authService;
        });

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var listResult = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert - Should handle filter exceptions gracefully
        Assert.NotNull(listResult);
        Assert.Empty(listResult.Tools); // Tools should be filtered out due to exception

        // CallTool should also fail gracefully
        var exception = await Assert.ThrowsAsync<McpException>(async () =>
            await client.CallToolAsync("read_tool", new { }, TestContext.Current.CancellationToken));
        
        Assert.Contains("error", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task ToolFiltering_WithDIRegisteredFilters_WorksCorrectly()
    {
        // Arrange
        using var serverTransport = new TestServerTransport();
        using var clientTransport = serverTransport.GetClientTransport();

        var serverOptions = CreateServerOptions();
        AddTestTools(serverOptions);
        
        // Register filters via DI
        serverOptions.ServiceCollection?.AddSingleton<IToolFilter>(new AllowAllToolFilter(priority: 100));
        serverOptions.ServiceCollection?.AddSingleton<IToolFilter>(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1));
        serverOptions.ServiceCollection?.AddSingleton<IToolAuthorizationService, ToolAuthorizationService>();

        using var server = McpServerFactory.CreateServer(serverTransport, serverOptions, LoggerFactory);
        using var client = McpClientFactory.CreateClient(clientTransport, LoggerFactory);

        await server.StartAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        var result = await client.ListToolsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Tools.Count); // Should filter out admin_tool
        Assert.Contains(result.Tools, t => t.Name == "read_tool");
        Assert.Contains(result.Tools, t => t.Name == "write_tool");
        Assert.DoesNotContain(result.Tools, t => t.Name == "admin_tool");
    }

    private static McpServerOptions CreateServerOptions()
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "TestServer", Version = "1.0.0" },
            ServiceCollection = new ServiceCollection()
        };

        // Add basic logging
        options.ServiceCollection.AddLogging();

        return options;
    }

    private static void AddTestTools(McpServerOptions options)
    {
        // Add test tools to the server
        options.Capabilities.Tools ??= new();
        options.Capabilities.Tools.ToolCollection ??= new();

        // Add read tool
        var readTool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [new TextResourceContents { Text = "Read operation completed" }]
        });
        readTool.ProtocolTool.Name = "read_tool";
        readTool.ProtocolTool.Description = "Reads data";
        options.Capabilities.Tools.ToolCollection.Add(readTool);

        // Add write tool
        var writeTool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [new TextResourceContents { Text = "Write operation completed" }]
        });
        writeTool.ProtocolTool.Name = "write_tool";
        writeTool.ProtocolTool.Description = "Writes data";
        options.Capabilities.Tools.ToolCollection.Add(writeTool);

        // Add admin tool
        var adminTool = McpServerTool.Create(() => new CallToolResult
        {
            Content = [new TextResourceContents { Text = "Admin operation completed" }]
        });
        adminTool.ProtocolTool.Name = "admin_tool";
        adminTool.ProtocolTool.Description = "Administrative operations";
        options.Capabilities.Tools.ToolCollection.Add(adminTool);
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
}