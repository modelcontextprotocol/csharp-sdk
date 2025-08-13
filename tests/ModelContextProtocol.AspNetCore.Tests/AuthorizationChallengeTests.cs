using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Server.Authorization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for authorization challenge functionality in MCP HTTP transport.
/// </summary>
public class AuthorizationChallengeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationChallengeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CallTool_WithAuthorizationDenied_ReturnsWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService();
                    // Add a filter that always denies with OAuth2 challenge
                    authService.RegisterFilter(new SampleOAuthToolFilter("write:tools", "mcp-server"));
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool());
            });
        }).CreateClient();

        // Create a call tool request
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
        
        // Set required Accept headers for MCP
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        var response = await client.PostAsync("/mcp", content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
        
        var wwwAuthHeader = response.Headers.GetValues("WWW-Authenticate").First();
        Assert.Contains("Bearer", wwwAuthHeader);
        Assert.Contains("scope=\"write:tools\"", wwwAuthHeader);
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
    public async Task CallTool_WithInvalidToken_ReturnsWwwAuthenticateHeader()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IToolAuthorizationService>(sp =>
                {
                    var authService = new ToolAuthorizationService();
                    // Add a filter that denies with invalid token challenge
                    authService.RegisterFilter(new AlwaysDenyFilter());
                    return authService;
                });
                
                services.AddScoped<McpServerTool>(sp => new TestTool());
            });
        }).CreateClient();

        // Create a call tool request for a tool that requires authentication
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
        
        // Set required Accept headers for MCP
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

        var responseContent = await response.Content.ReadAsStringAsync();
        var errorResponse = JsonSerializer.Deserialize<JsonRpcError>(responseContent, McpJsonUtilities.JsonContext.Default.JsonRpcError);
        
        Assert.NotNull(errorResponse.Error);
        Assert.Equal((int)McpErrorCode.InvalidParams, errorResponse.Error.Code);
        Assert.Contains("secure_tool", errorResponse.Error.Message);
    }

    /// <summary>
    /// Test tool for authorization challenge testing.
    /// </summary>
    private class TestTool : McpServerTool
    {
        public override Tool ProtocolTool => new()
        {
            Name = "test_tool",
            Description = "A test tool for authorization testing"
        };

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new CallToolResult
            {
                Content = [new TextResourceContents { Text = "Tool executed successfully" }]
            });
        }
    }

    /// <summary>
    /// Tool filter that always denies access with an invalid token challenge.
    /// </summary>
    private class AlwaysDenyFilter : IToolFilter
    {
        public int Priority => 1;

        public Task<AuthorizationResult> AuthorizeAsync(Tool tool, ToolAuthorizationContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AuthorizationResult.DenyInvalidToken("mcp-server"));
        }
    }
}