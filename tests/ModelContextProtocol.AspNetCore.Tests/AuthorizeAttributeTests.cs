using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.ComponentModel;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for MCP authorization functionality with [Authorize], [AllowAnonymous] and role-based authorization.
/// </summary>
public class AuthorizeAttributeTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{

    private async Task<McpClient> ConnectAsync()
    {
        await using var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new("http://localhost:5000"),
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken, loggerFactory: LoggerFactory);
    }

    [Fact]
    public async Task Authorize_Tool_RequiresAuthentication()
    {
        await using var app = await StartServerWithAuth(builder => builder.WithTools<AuthorizationTestTools>());

        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync(
                "authorized_tool",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Access forbidden: This tool requires authorization.", exception.Message);
        Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
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

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync(
                "admin_tool",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): Access forbidden: This tool requires authorization.", exception.Message);
        Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
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

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
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

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
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

    [Fact]
    public async Task ListTools_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithTools<AuthorizationTestTools>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for tools/list operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task CallTool_WithoutAuthFilters_ReturnsError()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithTools<AuthorizationTestTools>());
        var client = await ConnectAsync();

        var toolResult = await client.CallToolAsync(
                "authorized_tool",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(toolResult.IsError);

        var errorContent = Assert.IsType<TextContentBlock>(Assert.Single(toolResult.Content));
        Assert.Equal("An error occurred invoking 'authorized_tool'.", errorContent.Text);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Error &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for tools/call operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task ListPrompts_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithPrompts<AuthorizationTestPrompts>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for prompts/list operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task GetPrompt_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithPrompts<AuthorizationTestPrompts>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.GetPromptAsync(
                "authorized_prompt",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for prompts/get operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task ListResources_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithResources<AuthorizationTestResources>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for resources/list operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task ReadResource_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithResources<AuthorizationTestResources>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ReadResourceAsync(
                "resource://authorized",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for resources/read operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task ListResourceTemplates_WithoutAuthFilters_ThrowsInvalidOperationException()
    {
        await using var app = await StartServerWithoutAuthFilters(builder => builder.WithResources<AuthorizationTestResources>());
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("Request failed (remote): An error occurred.", exception.Message);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("Authorization filter was not invoked for resources/templates/list operation") &&
            log.Exception.Message.Contains("Ensure that AddAuthorizationFilters() is called"));
    }

    [Fact]
    public async Task ListTools_WithHandlerAndNullCollection_AllToolsVisible()
    {
        // When ToolCollection is null (custom handler only), the auth filter can't look up
        // primitives in the collection and should not filter any tools.
        await using var app = await StartServerWithAuth(builder =>
            builder.WithListToolsHandler(static (_, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools = [new Tool { Name = "custom_tool" }]
            })));

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Tool from custom handler (not in ToolCollection) should be visible even to anonymous users
        Assert.Single(tools);
        Assert.Equal("custom_tool", tools[0].Name);
    }

    [Fact]
    public async Task ListTools_WithMixedCollectionAndHandler_HandlerToolsNotFiltered()
    {
        // Tools in the ToolCollection are filtered based on auth metadata.
        // Tools returned only from a custom handler (not in ToolCollection) are not filtered.
        await using var app = await StartServerWithAuth(builder =>
        {
            builder.WithTools<AuthorizationTestTools>();
            builder.WithListToolsHandler(static (_, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools = [new Tool { Name = "handler_tool" }]
            }));
        });

        var client = await ConnectAsync();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Anonymous user: anonymous_tool from collection + handler_tool (not in collection, so not filtered)
        Assert.Equal(2, tools.Count);
        var toolNames = tools.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["anonymous_tool", "handler_tool"], toolNames);
    }

    [Fact]
    public async Task ListPrompts_WithHandlerAndNullCollection_AllPromptsVisible()
    {
        // When PromptCollection is null (custom handler only), the auth filter can't look up
        // primitives in the collection and should not filter any prompts.
        await using var app = await StartServerWithAuth(builder =>
            builder.WithListPromptsHandler(static (_, _) => ValueTask.FromResult(new ListPromptsResult
            {
                Prompts = [new Prompt { Name = "custom_prompt" }]
            })));

        var client = await ConnectAsync();
        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Prompt from custom handler (not in PromptCollection) should be visible even to anonymous users
        Assert.Single(prompts);
        Assert.Equal("custom_prompt", prompts[0].Name);
    }

    [Fact]
    public async Task ListResources_WithHandlerAndNullCollection_AllResourcesVisible()
    {
        // When ResourceCollection is null (custom handler only), the auth filter can't look up
        // primitives in the collection and should not filter any resources.
        await using var app = await StartServerWithAuth(builder =>
            builder.WithListResourcesHandler(static (_, _) => ValueTask.FromResult(new ListResourcesResult
            {
                Resources = [new Resource { Name = "custom_resource", Uri = "resource://custom" }]
            })));

        var client = await ConnectAsync();
        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Resource from custom handler (not in ResourceCollection) should be visible even to anonymous users
        Assert.Single(resources);
        Assert.Equal("resource://custom", resources[0].Uri);
    }

    [Fact]
    public async Task ListResourceTemplates_WithHandlerAndNullCollection_AllResourceTemplatesVisible()
    {
        // When ResourceCollection is null (custom handler only), the auth filter can't look up
        // primitives in the collection and should not filter any resource templates.
        await using var app = await StartServerWithAuth(builder =>
            builder.WithListResourceTemplatesHandler(static (_, _) => ValueTask.FromResult(new ListResourceTemplatesResult
            {
                ResourceTemplates = [new ResourceTemplate { Name = "custom_template", UriTemplate = "resource://custom/{id}" }]
            })));

        var client = await ConnectAsync();
        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Template from custom handler (not in ResourceCollection) should be visible even to anonymous users
        Assert.Single(templates);
        Assert.Equal("resource://custom/{id}", templates[0].UriTemplate);
    }

    private async Task<WebApplication> StartServerWithAuth(Action<IMcpServerBuilder> configure, string? userName = null, params string[] roles)
    {
        var mcpServerBuilder = Builder.Services.AddMcpServer().WithHttpTransport().AddAuthorizationFilters();
        configure(mcpServerBuilder);

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

    private async Task<WebApplication> StartServerWithoutAuthFilters(Action<IMcpServerBuilder> configure)
    {
        var mcpServerBuilder = Builder.Services.AddMcpServer().WithHttpTransport(); // No AddAuthorizationFilters() call
        configure(mcpServerBuilder);

        Builder.Services.AddAuthorization();

        var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private ClaimsPrincipal CreateUser(string name, params string[] roles)
        => new(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name), .. roles.Select(role => new Claim("role", role))],
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

        [McpServerResource(UriTemplate = "resource://authorized/{id}"), Description("A resource template that requires authorization.")]
        [Authorize]
        public static string AuthorizedResourceWithTemplate(string id)
        {
            return "Authorized resource content";
        }
    }
}
