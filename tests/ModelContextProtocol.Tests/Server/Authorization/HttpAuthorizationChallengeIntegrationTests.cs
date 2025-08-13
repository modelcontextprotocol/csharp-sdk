using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ModelContextProtocol.Tests.Server.Authorization;

/// <summary>
/// Integration tests for CallTool authorization with HTTP challenges.
/// </summary>
public class HttpAuthorizationChallengeIntegrationTests : IClassFixture<WebApplicationFactory<TestProgram>>
{
    private readonly WebApplicationFactory<TestProgram> _factory;

    public HttpAuthorizationChallengeIntegrationTests(WebApplicationFactory<TestProgram> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CallTool_WithInsufficientScopeFilter_ReturnsWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestOAuthToolFilter("write:admin", "mcp-server"));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("admin_delete_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-1"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "admin_delete_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        Assert.Contains("Bearer", wwwAuthHeader);
        Assert.Contains("scope=\"write:admin\"", wwwAuthHeader);
        Assert.Contains("error=\"insufficient_scope\"", wwwAuthHeader);
        Assert.Contains("realm=\"mcp-server\"", wwwAuthHeader);

        var responseContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonRpcError>(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcError);
        
        Assert.NotNull(errorResponse.Error);
        Assert.Equal((int)McpErrorCode.InvalidParams, errorResponse.Error.Code);
        Assert.Contains("admin_delete_tool", errorResponse.Error.Message);
        Assert.Contains("Insufficient scope", errorResponse.Error.Message);
    }

    [Fact]
    public async Task CallTool_WithInvalidTokenFilter_ReturnsWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestInvalidTokenFilter("mcp-server"));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("secure_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-2"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "secure_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        Assert.Contains("Bearer", wwwAuthHeader);
        Assert.Contains("error=\"invalid_token\"", wwwAuthHeader);
        Assert.Contains("realm=\"mcp-server\"", wwwAuthHeader);

        var responseContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonRpcError>(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcError);
        
        Assert.NotNull(errorResponse.Error);
        Assert.Equal((int)McpErrorCode.InvalidParams, errorResponse.Error.Code);
        Assert.Contains("secure_tool", errorResponse.Error.Message);
        Assert.Contains("Invalid or expired token", errorResponse.Error.Message);
    }

    [Fact]
    public async Task CallTool_WithBasicAuthChallenge_ReturnsBasicWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestBasicAuthFilter("secure-area"));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("protected_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-3"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "protected_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        Assert.Contains("Basic", wwwAuthHeader);
        Assert.Contains("realm=\"secure-area\"", wwwAuthHeader);

        var responseContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonRpcError>(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcError);
        
        Assert.NotNull(errorResponse.Error);
        Assert.Equal((int)McpErrorCode.InvalidParams, errorResponse.Error.Code);
        Assert.Contains("protected_tool", errorResponse.Error.Message);
    }

    [Fact]
    public async Task CallTool_WithCustomAuthChallenge_ReturnsCustomWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new TestCustomAuthFilter());
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("api_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-4"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "api_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        Assert.Contains("ApiKey", wwwAuthHeader);
        Assert.Contains("realm=\"api\"", wwwAuthHeader);
        Assert.Contains("scope=\"full\"", wwwAuthHeader);
    }

    [Fact]
    public async Task CallTool_WithAllowedTool_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    authService.RegisterFilter(new AllowAllToolFilter());
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("allowed_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-5"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "allowed_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));

        var responseContent = await response.Content.ReadAsStringAsync();
        var successResponse = JsonSerializer.Deserialize<JsonRpcResponse>(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcResponse);
        
        Assert.NotNull(successResponse.Result);
        Assert.Null(successResponse.Error);
    }

    [Fact]
    public async Task CallTool_WithMultipleFilters_ReturnsFirstDenyChallenge()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService(sp.GetService<ILogger<ToolAuthorizationService>>());
                    // First filter (higher priority) denies with OAuth challenge
                    authService.RegisterFilter(new TestOAuthToolFilter("write:admin", "mcp-server", priority: 1));
                    // Second filter (lower priority) would deny with Basic auth, but shouldn't be reached
                    authService.RegisterFilter(new TestBasicAuthFilter("secure-area", priority: 2));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool("admin_tool"));
            });
        }).CreateClient();

        var callToolRequest = new JsonRpcRequest
        {
            Id = RequestId.FromString("test-6"),
            Method = RequestMethods.ToolsCall,
            Params = new CallToolRequestParams
            {
                Name = "admin_tool",
                Arguments = new Dictionary<string, object>()
            }
        };

        var requestJson = JsonSerializer.Serialize(callToolRequest, McpJsonUtilities.JsonContext.Default.JsonRpcRequest);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        // Should get OAuth challenge from first filter, not Basic from second filter
        Assert.Contains("Bearer", wwwAuthHeader);
        Assert.Contains("scope=\"write:admin\"", wwwAuthHeader);
        Assert.DoesNotContain("Basic", wwwAuthHeader);
    }

    /// <summary>
    /// Test tool for authorization challenge testing.
    /// </summary>
    private class TestTool : McpServerTool
    {
        private readonly string _toolName;

        public TestTool(string toolName = "test_tool")
        {
            _toolName = toolName;
        }

        public override Tool ProtocolTool => new()
        {
            Name = _toolName,
            Description = $"A test tool for authorization testing: {_toolName}"
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextResourceContents { Text = $"Tool {_toolName} executed successfully" }]
            });
        }
    }

    /// <summary>
    /// Test OAuth tool filter that demonstrates insufficient scope challenges.
    /// </summary>
    private class TestOAuthToolFilter : IToolFilter
    {
        private readonly string _requiredScope;
        private readonly string? _realm;

        public TestOAuthToolFilter(string requiredScope, string? realm = null, int priority = 100)
        {
            _requiredScope = requiredScope ?? throw new ArgumentNullException(nameof(requiredScope));
            _realm = realm;
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
            // For testing, always deny with insufficient scope challenge
            return Task.FromResult(AuthorizationResult.DenyInsufficientScope(_requiredScope, _realm));
        }
    }

    /// <summary>
    /// Test filter that demonstrates invalid token challenges.
    /// </summary>
    private class TestInvalidTokenFilter : IToolFilter
    {
        private readonly string? _realm;

        public TestInvalidTokenFilter(string? realm = null)
        {
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
            // For testing, always deny with invalid token challenge
            return Task.FromResult(AuthorizationResult.DenyInvalidToken(_realm));
        }
    }

    /// <summary>
    /// Test filter that demonstrates Basic auth challenges.
    /// </summary>
    private class TestBasicAuthFilter : IToolFilter
    {
        private readonly string? _realm;

        public TestBasicAuthFilter(string? realm = null, int priority = 100)
        {
            _realm = realm;
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
            // For testing, always deny with Basic auth challenge
            return Task.FromResult(AuthorizationResult.DenyWithBasicChallenge("Authentication required", _realm));
        }
    }

    /// <summary>
    /// Test filter that demonstrates custom auth challenges.
    /// </summary>
    private class TestCustomAuthFilter : IToolFilter
    {
        public int Priority => 100;

        public async Task<bool> ShouldIncludeToolAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            var result = await CanExecuteToolAsync(tool.Name, context, cancellationToken);
            return result.IsAuthorized;
        }

        public Task<AuthorizationResult> CanExecuteToolAsync(string toolName, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            // For testing, always deny with custom auth challenge
            var challenge = AuthorizationChallenge.CreateCustomChallenge("ApiKey", 
                ("realm", "api"), 
                ("scope", "full"));
            return Task.FromResult(AuthorizationResult.DenyWithChallenge("API key required", challenge));
        }
    }
}

/// <summary>
/// Test program for the web application factory.
/// </summary>
public class TestProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMcp();
        
        var app = builder.Build();
        app.MapMcp("/mcp");
        
        app.Run();
    }
}