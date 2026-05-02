using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for built-in incremental scope consent (SEP-835) support.
/// When <c>AddAuthorizationFilters()</c> is NOT called, the HTTP transport performs a pre-flight
/// authorization check that returns HTTP 403 with <c>WWW-Authenticate: Bearer error="insufficient_scope"</c>
/// to trigger client re-authentication with broader scopes.
/// </summary>
public class IncrementalConsentTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    private const string InitializeJson = """
        {
            "jsonrpc": "2.0",
            "id": 0,
            "method": "initialize",
            "params": {
                "protocolVersion": "2025-03-26",
                "capabilities": {},
                "clientInfo": { "name": "test", "version": "0.1" }
            }
        }
        """;

    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost:5000"),
        }, HttpClient, LoggerFactory);

        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken, loggerFactory: LoggerFactory);
    }

    // -------------------------
    // Listings: all primitives visible
    // -------------------------

    [Fact]
    public async Task ListTools_WithoutAuthFilters_ReturnsAllToolsIncludingAuthorized()
    {
        await using var app = await StartServerAsync(builder => builder.WithTools<ScopedTools>());
        var client = await ConnectAsync();

        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // All tools should be visible — [Authorize] does NOT hide tools when AddAuthorizationFilters() is not called.
        Assert.Equal(2, tools.Count);
        var toolNames = tools.Select(t => t.Name).OrderBy(n => n).ToList();
        Assert.Equal(["public_tool", "scoped_tool"], toolNames);
    }

    [Fact]
    public async Task ListPrompts_WithoutAuthFilters_ReturnsAllPromptsIncludingAuthorized()
    {
        await using var app = await StartServerAsync(builder => builder.WithPrompts<ScopedPrompts>());
        var client = await ConnectAsync();

        var prompts = await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, prompts.Count);
        var promptNames = prompts.Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(["public_prompt", "scoped_prompt"], promptNames);
    }

    [Fact]
    public async Task ListResources_WithoutAuthFilters_ReturnsAllResourcesIncludingAuthorized()
    {
        await using var app = await StartServerAsync(builder => builder.WithResources<ScopedResources>());
        var client = await ConnectAsync();

        var resources = await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, resources.Count);
        var uris = resources.Select(r => r.Uri).OrderBy(u => u).ToList();
        Assert.Equal(["resource://public", "resource://scoped"], uris);
    }

    [Fact]
    public async Task ListResourceTemplates_WithoutAuthFilters_ReturnsAllTemplatesIncludingAuthorized()
    {
        await using var app = await StartServerAsync(builder => builder.WithResources<ScopedResources>());
        var client = await ConnectAsync();

        var templates = await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(templates);
        Assert.Equal("resource://scoped/{id}", templates[0].UriTemplate);
    }

    // -------------------------
    // Invocations: unauthorized → HTTP 403 with WWW-Authenticate header
    // -------------------------

    [Fact]
    public async Task CallTool_Unauthorized_Returns403WithInsufficientScopeHeader()
    {
        await using var app = await StartServerAsync(builder => builder.WithTools<ScopedTools>());

        var sessionId = await InitializeSessionAsync();

        var callToolJson = """
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "tools/call",
                "params": { "name": "scoped_tool", "arguments": { "message": "test" } }
            }
            """;

        using var response = await PostJsonRpcAsync(callToolJson, sessionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify WWW-Authenticate header contains the insufficient_scope error.
        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("Bearer", wwwAuth);
        Assert.Contains("insufficient_scope", wwwAuth);
        Assert.Contains("read_data", wwwAuth); // scope from [Authorize(Roles = "read_data")]
        Assert.Contains("resource_metadata", wwwAuth);
    }

    [Fact]
    public async Task GetPrompt_Unauthorized_Returns403WithInsufficientScopeHeader()
    {
        await using var app = await StartServerAsync(builder => builder.WithPrompts<ScopedPrompts>());

        var sessionId = await InitializeSessionAsync();

        var getPromptJson = """
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "prompts/get",
                "params": { "name": "scoped_prompt", "arguments": { "message": "test" } }
            }
            """;

        using var response = await PostJsonRpcAsync(getPromptJson, sessionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("insufficient_scope", wwwAuth);
    }

    [Fact]
    public async Task ReadResource_Unauthorized_Returns403WithInsufficientScopeHeader()
    {
        await using var app = await StartServerAsync(builder => builder.WithResources<ScopedResources>());

        var sessionId = await InitializeSessionAsync();

        var readResourceJson = """
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "resources/read",
                "params": { "uri": "resource://scoped" }
            }
            """;

        using var response = await PostJsonRpcAsync(readResourceJson, sessionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("insufficient_scope", wwwAuth);
    }

    // -------------------------
    // Authorized user: invocation succeeds
    // -------------------------

    [Fact]
    public async Task CallTool_AuthorizedUser_Succeeds()
    {
        await using var app = await StartServerAsync(builder => builder.WithTools<ScopedTools>(), userName: "authorized-user", roles: ["read_data"]);
        var client = await ConnectAsync();

        var result = await client.CallToolAsync(
            "scoped_tool",
            new Dictionary<string, object?> { ["message"] = "hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Scoped: hello", content.Text);
    }

    [Fact]
    public async Task CallTool_WrongRole_Returns403()
    {
        await using var app = await StartServerAsync(builder => builder.WithTools<ScopedTools>(), userName: "wrong-role-user", roles: ["wrong_scope"]);
        var client = await ConnectAsync();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await client.CallToolAsync(
                "scoped_tool",
                new Dictionary<string, object?> { ["message"] = "hello" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task CallTool_PublicTool_AlwaysSucceeds()
    {
        await using var app = await StartServerAsync(builder => builder.WithTools<ScopedTools>());
        var client = await ConnectAsync();

        // Public tool (no [Authorize]) should succeed without authentication.
        var result = await client.CallToolAsync(
            "public_tool",
            new Dictionary<string, object?> { ["message"] = "hello" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var content = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Public: hello", content.Text);
    }

    // -------------------------
    // AddAuthorizationFilters behavior is unchanged
    // -------------------------

    [Fact]
    public async Task ListTools_WithAuthFilters_FiltersUnauthorizedTools()
    {
        await using var app = await StartServerWithAuthFiltersAsync(builder => builder.WithTools<ScopedTools>());
        var client = await ConnectAsync();

        // With AddAuthorizationFilters(), unauthorized tools are hidden from listings.
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(tools);
        Assert.Equal("public_tool", tools[0].Name);
    }

    [Fact]
    public async Task CallTool_WithAuthFilters_ReturnsJsonRpcError()
    {
        await using var app = await StartServerWithAuthFiltersAsync(builder => builder.WithTools<ScopedTools>());
        var client = await ConnectAsync();

        // With AddAuthorizationFilters(), unauthorized invocation returns a JSON-RPC error (not HTTP 403).
        McpProtocolException? exception = null;
        try
        {
            await client.CallToolAsync(
                "scoped_tool",
                new Dictionary<string, object?> { ["message"] = "test" },
                cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (McpProtocolException ex)
        {
            exception = ex;
        }

        Assert.NotNull(exception);
        Assert.Contains("Access forbidden", exception.Message);
    }

    // -------------------------
    // Helpers
    // -------------------------

    private async Task<WebApplication> StartServerAsync(Action<IMcpServerBuilder> configure, string? userName = null, params string[] roles)
    {
        var mcpServerBuilder = Builder.Services.AddMcpServer().WithHttpTransport(); // No AddAuthorizationFilters()
        configure(mcpServerBuilder);

        Builder.Services.AddAuthorization();

        var app = Builder.Build();

        if (userName is not null)
        {
            app.Use(next => async context =>
            {
                context.User = CreateUser(userName, roles);
                await next(context);
            });
        }

        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private async Task<WebApplication> StartServerWithAuthFiltersAsync(Action<IMcpServerBuilder> configure)
    {
        var mcpServerBuilder = Builder.Services.AddMcpServer().WithHttpTransport().AddAuthorizationFilters();
        configure(mcpServerBuilder);

        Builder.Services.AddAuthorization();

        var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private async Task<string?> InitializeSessionAsync()
    {
        using var response = await PostJsonRpcAsync(InitializeJson, sessionId: null);
        Assert.True(response.IsSuccessStatusCode, $"Initialize failed with {response.StatusCode}");
        return response.Headers.TryGetValues("Mcp-Session-Id", out var ids) ? ids.FirstOrDefault() : null;
    }

    private async Task<HttpResponseMessage> PostJsonRpcAsync(string json, string? sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return await HttpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static ClaimsPrincipal CreateUser(string name, params string[] roles)
        => new(new ClaimsIdentity(
            [new Claim("name", name), new Claim(ClaimTypes.NameIdentifier, name), .. roles.Select(role => new Claim("role", role))],
            "TestAuthType", "name", "role"));

    [McpServerToolType]
    private class ScopedTools
    {
        [McpServerTool, Description("A public tool that requires no authorization.")]
        public static string PublicTool(string message) => $"Public: {message}";

        [McpServerTool, Description("A tool that requires the read_data scope.")]
        [Authorize(Roles = "read_data")]
        public static string ScopedTool(string message) => $"Scoped: {message}";
    }

    [McpServerPromptType]
    private class ScopedPrompts
    {
        [McpServerPrompt, Description("A public prompt.")]
        public static string PublicPrompt(string message) => $"Public prompt: {message}";

        [McpServerPrompt, Description("A prompt that requires the read_data scope.")]
        [Authorize(Roles = "read_data")]
        public static string ScopedPrompt(string message) => $"Scoped prompt: {message}";
    }

    [McpServerResourceType]
    private class ScopedResources
    {
        [McpServerResource(UriTemplate = "resource://public"), Description("A public resource.")]
        public static string PublicResource() => "Public resource content";

        [McpServerResource(UriTemplate = "resource://scoped"), Description("A scoped resource.")]
        [Authorize(Roles = "read_data")]
        public static string ScopedResource() => "Scoped resource content";

        [McpServerResource(UriTemplate = "resource://scoped/{id}"), Description("A scoped resource template.")]
        [Authorize(Roles = "read_data")]
        public static string ScopedResourceTemplate(string id) => $"Scoped resource content: {id}";
    }
}
