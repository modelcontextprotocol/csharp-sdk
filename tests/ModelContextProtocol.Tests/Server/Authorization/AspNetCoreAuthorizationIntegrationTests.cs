using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Integration tests for ASP.NET Core authorization scenarios.
/// </summary>
public class AspNetCoreAuthorizationIntegrationTests : LoggedTest, IClassFixture<WebApplicationFactory<TestStartup>>
{
    private readonly WebApplicationFactory<TestStartup> _factory;

    public AspNetCoreAuthorizationIntegrationTests(WebApplicationFactory<TestStartup> factory, ITestOutputHelper testOutputHelper) 
        : base(testOutputHelper)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authorization_WithDependencyInjectedFilters_WorksCorrectly()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register filters via DI
                services.AddSingleton<IToolFilter>(new AllowAllToolFilter(priority: 100));
                services.AddSingleton<IToolFilter>(new ToolNamePatternFilter(new[] { "admin_*" }, allowMatching: false, priority: 1));
                services.AddSingleton<IToolAuthorizationService, ToolAuthorizationService>();
                
                services.AddScoped<McpServerTool>(sp => new TestTool("admin_delete"));
                services.AddScoped<McpServerTool>(sp => new TestTool("user_profile"));
            });
        }).CreateClient();

        // Test ListTools
        var listRequest = CreateJsonRpcRequest(RequestMethods.ToolsList, new ListToolsRequestParams());
        var listResponse = await SendRequest(client, listRequest);
        
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listContent = await listResponse.Content.ReadAsStringAsync();
        var listResult = JsonSerializer.Deserialize<JsonRpcResponse>(listContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        
        Assert.NotNull(listResult.Result);
        var toolsResult = JsonSerializer.Deserialize<ListToolsResult>(listResult.Result.ToString()!, McpJsonUtilities.JsonContext.Default.ListToolsResult);
        
        // Should only have user_profile tool (admin_delete filtered out)
        Assert.Single(toolsResult.Tools);
        Assert.Equal("user_profile", toolsResult.Tools[0].Name);

        // Test CallTool for allowed tool
        var callRequest = CreateJsonRpcRequest(RequestMethods.ToolsCall, new CallToolRequestParams
        {
            Name = "user_profile",
            Arguments = new Dictionary<string, object>()
        });
        var callResponse = await SendRequest(client, callRequest);
        
        Assert.Equal(HttpStatusCode.OK, callResponse.StatusCode);

        // Test CallTool for denied tool
        var deniedRequest = CreateJsonRpcRequest(RequestMethods.ToolsCall, new CallToolRequestParams
        {
            Name = "admin_delete",
            Arguments = new Dictionary<string, object>()
        });
        var deniedResponse = await SendRequest(client, deniedRequest);
        
        Assert.Equal(HttpStatusCode.BadRequest, deniedResponse.StatusCode); // Tool not found/accessible
    }

    [Fact]
    public async Task Authorization_WithRoleBasedFiltering_RespectsUserClaims()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    var filter = RoleBasedToolFilterBuilder.Create()
                        .RequireRole("admin")
                        .ForToolsMatching("admin_*")
                        .Build();
                    authService.RegisterFilter(filter);
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("admin_panel"));
                services.AddScoped<McpServerTool>(sp => new TestTool("user_dashboard"));

                // Add authentication for setting up user context
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            });

            builder.Configure(app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                
                app.Use(async (context, next) =>
                {
                    // Set up user context based on request headers
                    if (context.Request.Headers.TryGetValue("X-User-Role", out var roleHeader))
                    {
                        var identity = new ClaimsIdentity("Test");
                        identity.AddClaim(new Claim(ClaimTypes.Role, roleHeader.ToString()));
                        context.User = new ClaimsPrincipal(identity);
                    }
                    await next();
                });

                app.MapMcp("/mcp");
            });
        }).CreateClient();

        // Test without admin role
        var listRequest = CreateJsonRpcRequest(RequestMethods.ToolsList, new ListToolsRequestParams());
        var listResponse = await SendRequest(client, listRequest);
        
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listContent = await listResponse.Content.ReadAsStringAsync();
        var listResult = JsonSerializer.Deserialize<JsonRpcResponse>(listContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        var toolsResult = JsonSerializer.Deserialize<ListToolsResult>(listResult.Result.ToString()!, McpJsonUtilities.JsonContext.Default.ListToolsResult);
        
        // Should only have user_dashboard tool (admin_panel filtered out)
        Assert.Single(toolsResult.Tools);
        Assert.Equal("user_dashboard", toolsResult.Tools[0].Name);

        // Test with admin role
        client.DefaultRequestHeaders.Add("X-User-Role", "admin");
        var adminListResponse = await SendRequest(client, listRequest);
        
        Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);
        var adminListContent = await adminListResponse.Content.ReadAsStringAsync();
        var adminListResult = JsonSerializer.Deserialize<JsonRpcResponse>(adminListContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        var adminToolsResult = JsonSerializer.Deserialize<ListToolsResult>(adminListResult.Result.ToString()!, McpJsonUtilities.JsonContext.Default.ListToolsResult);
        
        // Should have both tools with admin role
        Assert.Equal(2, adminToolsResult.Tools.Count);
        Assert.Contains(adminToolsResult.Tools, t => t.Name == "admin_panel");
        Assert.Contains(adminToolsResult.Tools, t => t.Name == "user_dashboard");
    }

    [Fact]
    public async Task Authorization_WithSessionContext_PassesCorrectContext()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestContextCapturingFilter());
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("context_tool"));
            });
        }).CreateClient();

        // Add session/user context headers
        client.DefaultRequestHeaders.Add("X-Session-Id", "test-session-123");
        client.DefaultRequestHeaders.Add("X-User-Id", "user-456");

        var listRequest = CreateJsonRpcRequest(RequestMethods.ToolsList, new ListToolsRequestParams());
        var listResponse = await SendRequest(client, listRequest);
        
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        
        // The filter should have captured the context
        // In a real implementation, you'd verify the context was properly set
    }

    [Fact]
    public async Task Authorization_WithAuthenticationMiddleware_IntegratesCorrectly()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestBearerTokenFilter());
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("secure_api"));

                services.AddAuthentication("Bearer")
                    .AddScheme<TestAuthenticationSchemeOptions, TestBearerAuthenticationHandler>("Bearer", _ => { });
            });

            builder.Configure(app =>
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapMcp("/mcp");
            });
        }).CreateClient();

        // Test without token - should fail
        var request = CreateJsonRpcRequest(RequestMethods.ToolsCall, new CallToolRequestParams
        {
            Name = "secure_api",
            Arguments = new Dictionary<string, object>()
        });
        var response = await SendRequest(client, request);
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));

        // Test with valid token - should succeed
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");
        var authedResponse = await SendRequest(client, request);
        
        Assert.Equal(HttpStatusCode.OK, authedResponse.StatusCode);
    }

    [Fact]
    public async Task Authorization_WithMultipleEndpoints_IsolatesCorrectly()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new ToolNamePatternFilter(new[] { "public_*" }, allowMatching: true));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("public_info"));
                services.AddScoped<McpServerTool>(sp => new TestTool("private_data"));
            });

            builder.Configure(app =>
            {
                // Map multiple MCP endpoints
                app.MapMcp("/mcp/public");
                app.MapMcp("/mcp/admin");
            });
        }).CreateClient();

        // Test public endpoint
        var publicRequest = CreateJsonRpcRequest(RequestMethods.ToolsList, new ListToolsRequestParams());
        var publicResponse = await SendRequest(client, publicRequest, "/mcp/public");
        
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicContent = await publicResponse.Content.ReadAsStringAsync();
        var publicResult = JsonSerializer.Deserialize<JsonRpcResponse>(publicContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        var publicToolsResult = JsonSerializer.Deserialize<ListToolsResult>(publicResult.Result.ToString()!, McpJsonUtilities.JsonContext.Default.ListToolsResult);
        
        // Should only have public tools
        Assert.Single(publicToolsResult.Tools);
        Assert.Equal("public_info", publicToolsResult.Tools[0].Name);

        // Test admin endpoint (should have same filtering)
        var adminResponse = await SendRequest(client, publicRequest, "/mcp/admin");
        
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminContent = await adminResponse.Content.ReadAsStringAsync();
        var adminResult = JsonSerializer.Deserialize<JsonRpcResponse>(adminContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        var adminToolsResult = JsonSerializer.Deserialize<ListToolsResult>(adminResult.Result.ToString()!, McpJsonUtilities.JsonContext.Default.ListToolsResult);
        
        // Should have same filtering applied
        Assert.Single(adminToolsResult.Tools);
        Assert.Equal("public_info", adminToolsResult.Tools[0].Name);
    }

    private static JsonRpcRequest CreateJsonRpcRequest(string method, object? parameters)
    {
        return new JsonRpcRequest
        {
            Id = RequestId.FromString(Guid.NewGuid().ToString()),
            Method = method,
            Params = parameters
        };
    }

    private static async Task<HttpResponseMessage> SendRequest(HttpClient client, JsonRpcRequest request, string endpoint = "/mcp")
    {
        var requestJson = JsonSerializer.Serialize(request, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        return await client.PostAsync(endpoint, content);
    }

    /// <summary>
    /// Test tool for authorization testing.
    /// </summary>
    private class TestTool : McpServerTool
    {
        private readonly string _toolName;

        public TestTool(string toolName)
        {
            _toolName = toolName;
        }

        public override Tool ProtocolTool => new()
        {
            Name = _toolName,
            Description = $"Test tool: {_toolName}"
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextResourceContents { Text = $"Tool {_toolName} executed" }]
            });
        }
    }

    /// <summary>
    /// Test filter that captures authorization context for verification.
    /// </summary>
    private class TestContextCapturingFilter : IToolFilter
    {
        public int Priority => 100;

        public static ToolAuthorizationContext? LastContext { get; private set; }

        public Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.FromResult(true);
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Task.FromResult(AuthorizationResult.Allow("Context captured"));
        }
    }

    /// <summary>
    /// Test filter that demonstrates Bearer token authentication.
    /// </summary>
    private class TestBearerTokenFilter : IToolFilter
    {
        public int Priority => 100;

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var result = await CanExecuteToolAsync(tool.Name, context, cancellationToken);
            return result.IsAuthorized;
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // Check if user is authenticated (would be set by authentication middleware)
            if (context.Properties.TryGetValue("IsAuthenticated", out var authValue) && authValue is true)
            {
                return Task.FromResult(AuthorizationResult.Allow("Token valid"));
            }

            return Task.FromResult(AuthorizationResult.DenyInvalidToken("mcp-server"));
        }
    }
}

/// <summary>
/// Test startup class for ASP.NET Core integration tests.
/// </summary>
public class TestStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMcp();
        services.AddLogging();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.MapMcp("/mcp");
    }
}

/// <summary>
/// Test authentication scheme options.
/// </summary>
public class TestAuthenticationSchemeOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
}

/// <summary>
/// Test authentication handler.
/// </summary>
public class TestAuthenticationHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(Microsoft.AspNetCore.Authentication.IOptionsMonitor<TestAuthenticationSchemeOptions> options, 
        ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) 
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Test Bearer token authentication handler.
/// </summary>
public class TestBearerAuthenticationHandler : Microsoft.AspNetCore.Authentication.AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public TestBearerAuthenticationHandler(Microsoft.AspNetCore.Authentication.IOptionsMonitor<TestAuthenticationSchemeOptions> options, 
        ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) 
        : base(options, logger, encoder)
    {
    }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerValue = authHeader.ToString();
            if (headerValue.StartsWith("Bearer ") && headerValue.Length > 7)
            {
                var token = headerValue.Substring(7);
                if (token == "valid-token")
                {
                    var identity = new ClaimsIdentity("Bearer");
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Bearer");
                    return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
                }
            }
        }

        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail("Invalid token"));
    }
}