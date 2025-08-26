using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for MCP authorization functionality with [Authorize], [AllowAnonymous] and role-based authorization.
/// </summary>
public class AuthorizeAttributeTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private async Task<IMcpClient> ConnectAsync()
    {
        await using var transport = new SseClientTransport(new SseClientTransportOptions
        {
            Endpoint = new("http://localhost:5000"),
        }, HttpClient, LoggerFactory);

        return await McpClientFactory.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken, loggerFactory: LoggerFactory);
    }

    [Fact]
    public async Task Authorize_Tool_RequiresAuthentication()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>());

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "authorized_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Should return error because tool requires authorization but user is anonymous
        Assert.True(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Access forbidden: This tool requires authorization.", content.Text);
    }

    [Fact]
    public async Task ClassLevelAuthorize_Tool_RequiresAuthentication()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AllowAnonymousTestTools>());

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "anonymous_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Anonymous: test", content.Text);
    }

    [Fact]
    public async Task AllowAnonymous_Tool_AllowsAnonymousAccess()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AllowAnonymousTestTools>());

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "anonymous_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Anonymous: test", content.Text);
    }

    [Fact]
    public async Task Authorize_Tool_AllowsAuthenticatedUser()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "TestUser");

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "authorized_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Authorized: test", content.Text);
    }

    [Fact]
    public async Task AuthorizeWithRoles_Tool_RequiresAdminRole()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "TestUser", "User");

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "admin_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        // Should return error because tool requires Admin role but user only has User role
        Assert.True(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Access forbidden: This tool requires authorization.", content.Text);
    }

    [Fact]
    public async Task AuthorizeWithRoles_Tool_AllowsAdminUser()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "AdminUser", "Admin");

        var client = await ConnectAsync();
        var result = await client.CallToolAsync(
            "admin_tool",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Admin: test", content.Text);
    }

    [Fact]
    public async Task ListTools_Anonymous_OnlyReturnsAnonymousTools()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>());

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(tools);
        Assert.Equal("anonymous_tool", tools[0].Name);
    }

    [Fact]
    public async Task ListTools_AuthenticatedUser_ReturnsAuthorizedTools()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "TestUser");

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Authenticated user should see anonymous and basic authorized tools, but not admin-only tools
        Assert.Equal(2, tools.Count);
        var toolNames = tools.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["anonymous_tool", "authorized_tool"], toolNames);
    }

    [Fact]
    public async Task ListTools_AdminUser_ReturnsAllTools()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "AdminUser", "Admin");

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Admin user should see all tools
        Assert.Equal(3, tools.Count);
        var toolNames = tools.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["admin_tool", "anonymous_tool", "authorized_tool"], toolNames);
    }

    [Fact]
    public async Task ListTools_UserRole_DoesNotReturnAdminTools()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>(), "TestUser", "User");

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // User with User role should not see admin-only tools
        Assert.Equal(2, tools.Count);
        var toolNames = tools.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["anonymous_tool", "authorized_tool"], toolNames);
    }

    [Fact]
    public async Task Authorize_Prompt_RequiresAuthentication()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithPrompts<AuthorizationTestPrompts>());

        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpException>(async () =>
            await client.GetPromptAsync(
                "authorized_prompt",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Access forbidden: This prompt requires authorization.", exception.Message);
        Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task Authorize_Prompt_AllowsAuthenticatedUser()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithPrompts<AuthorizationTestPrompts>(), "TestUser");

        var client = await ConnectAsync();
        var result = await client.GetPromptAsync(
            "authorized_prompt",
            new Dictionary<string, object?> { ["message"] = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var content = Assert.IsType<TextContentBlock>(message.Content);
        Assert.Equal("Authorized prompt: test", content.Text);
    }

    [Fact]
    public async Task ListPrompts_Anonymous_OnlyReturnsAnonymousPrompts()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithPrompts<AuthorizationTestPrompts>());

        var client = await ConnectAsync();
        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Anonymous user should only see prompts marked with [AllowAnonymous]
        Assert.Single(prompts);
        Assert.Equal("anonymous_prompt", prompts[0].Name);
    }

    [Fact]
    public async Task Authorize_Resource_RequiresAuthentication()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithResources<AuthorizationTestResources>());

        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpException>(async () =>
            await client.ReadResourceAsync(
                "resource://authorized",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Access forbidden: This resource requires authorization.", exception.Message);
        Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task Authorize_Resource_AllowsAuthenticatedUser()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithResources<AuthorizationTestResources>(), "TestUser");

        var client = await ConnectAsync();
        var result = await client.ReadResourceAsync(
            "resource://authorized",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Contents.OfType<TextResourceContents>());
        Assert.Equal("Authorized resource content", content.Text);
    }

    [Fact]
    public async Task ListResources_Anonymous_OnlyReturnsAnonymousResources()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithResources<AuthorizationTestResources>());

        var client = await ConnectAsync();
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(resources);
        Assert.Equal("resource://anonymous", resources[0].Uri);
    }

    private async Task<WebApplication> StartServerWithAuth(Action<IMcpServerBuilder> configure, string? userName = null, params string[] roles)
    {
        var builder = Builder.Services.AddMcpServer().WithHttpTransport();
        configure(builder);
        Builder.Services.AddAuthorization();

        var app = Builder.Build();

        if (userName is not null)
        {
            app.Use(next =>
            {
                return async context =>
                {
                    context.User = CreateUser(userName, roles);
                    await next(context);
                };
            });
        }

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private ClaimsPrincipal CreateUser(string name, params string[] roles)
        => new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name), ..roles.Select(role => new Claim("role", role))],
            "TestAuthType", "name", "role"));

    [McpServerToolType]
    private class AuthorizationTestTools
    {
        [McpServerTool, Description("A tool that allows anonymous access.")]
        public static string AnonymousTool(string message)
        {
            return $"Anonymous: {message}";
        }

        [McpServerTool, Description("A tool that requires authorization.")]
        [Authorize]
        public static string AuthorizedTool(string message)
        {
            return $"Authorized: {message}";
        }

        [McpServerTool, Description("A tool that requires Admin role.")]
        [Authorize(Roles = "Admin")]
        public static string AdminTool(string message)
        {
            return $"Admin: {message}";
        }
    }

    [McpServerToolType]
    [Authorize]
    private class AllowAnonymousTestTools
    {
        [McpServerTool, Description("A tool that allows anonymous access.")]
        [AllowAnonymous]
        public static string AnonymousTool(string message)
        {
            return $"Anonymous: {message}";
        }

        [McpServerTool, Description("A tool that requires authorization.")]
        public static string AuthorizedTool(string message)
        {
            return $"Authorized: {message}";
        }
    }

    [McpServerPromptType]
    private class AuthorizationTestPrompts
    {
        [McpServerPrompt, Description("A prompt that allows anonymous access.")]
        public static string AnonymousPrompt(string message)
        {
            return $"Anonymous prompt: {message}";
        }

        [McpServerPrompt, Description("A prompt that requires authorization.")]
        [Authorize]
        public static string AuthorizedPrompt(string message)
        {
            return $"Authorized prompt: {message}";
        }
    }

    [McpServerResourceType]
    private class AuthorizationTestResources
    {
        [McpServerResource(UriTemplate = "resource://anonymous"), Description("A resource that allows anonymous access.")]
        public static string AnonymousResource()
        {
            return "Anonymous resource content";
        }

        [McpServerResource(UriTemplate = "resource://authorized"), Description("A resource that requires authorization.")]
        [Authorize]
        public static string AuthorizedResource()
        {
            return "Authorized resource content";
        }
    }
}
