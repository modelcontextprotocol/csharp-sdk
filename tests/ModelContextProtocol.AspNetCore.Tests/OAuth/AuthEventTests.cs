using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using System.Net;
using System.Net.Http.Json;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

/// <summary>
/// Tests for MCP authentication when resource metadata is provided via events rather than static configuration.
/// </summary>
public class AuthEventTests : OAuthTestBase
{
    public AuthEventTests(ITestOutputHelper outputHelper)
        : base(outputHelper, configureMcpMetadata: false)
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            // Note: ResourceMetadata is NOT set here - it will be provided via events
            options.ResourceMetadata = null;

            options.Events.OnResourceMetadataRequest = async context =>
            {
                // Dynamically provide the resource metadata
                context.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = McpServerUrl,
                    AuthorizationServers = { OAuthServerUrl },
                    ScopesSupported = ["mcp:tools"],
                };
                await Task.CompletedTask;
            };
        });
    }

    [Fact]
    public async Task CanAuthenticate_WithResourceMetadataFromEvent()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(
            new()
            {
                Endpoint = new(McpServerUrl),
                OAuth = new()
                {
                    ClientId = "demo-client",
                    ClientSecret = "demo-secret",
                    RedirectUri = new Uri("http://localhost:1179/callback"),
                    AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                },
            },
            HttpClient,
            LoggerFactory
        );

        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task CanAuthenticate_WithDynamicClientRegistration_FromEvent()
    {
        await using var app = await StartMcpServerAsync();

        DynamicClientRegistrationResponse? dcrResponse = null;

        await using var transport = new HttpClientTransport(
            new()
            {
                Endpoint = new(McpServerUrl),
                OAuth = new ClientOAuthOptions()
                {
                    RedirectUri = new Uri("http://localhost:1179/callback"),
                    AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                    Scopes = ["mcp:tools"],
                    DynamicClientRegistration = new()
                    {
                        ClientName = "Test MCP Client",
                        ClientUri = new Uri("https://example.com"),
                        ResponseDelegate = (response, cancellationToken) =>
                        {
                            dcrResponse = response;
                            return Task.CompletedTask;
                        },
                    },
                },
            },
            HttpClient,
            LoggerFactory
        );

        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.NotNull(dcrResponse);
        Assert.False(string.IsNullOrEmpty(dcrResponse.ClientId));
        Assert.False(string.IsNullOrEmpty(dcrResponse.ClientSecret));
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_ReturnsCorrectMetadata_FromEvent()
    {
        await using var app = await StartMcpServerAsync();

        // Make a direct request to the resource metadata endpoint
        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(metadata);
        Assert.Equal(McpServerUrl, metadata.Resource);
        Assert.Contains(OAuthServerUrl, metadata.AuthorizationServers);
        Assert.Contains("mcp:tools", metadata.ScopesSupported);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_CanModifyExistingMetadata_InEvent()
    {
        // Override the configuration to test modification of existing metadata
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                // Set initial metadata
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = McpServerUrl,
                    AuthorizationServers = { OAuthServerUrl },
                    ScopesSupported = ["mcp:basic"],
                };

                // Override the event to modify the metadata
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Start with the existing metadata and modify it
                    if (context.ResourceMetadata != null)
                    {
                        context.ResourceMetadata.ScopesSupported.Add("mcp:tools");
                        context.ResourceMetadata.ResourceName = "Dynamic Test Resource";
                    }
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = await StartMcpServerAsync();

        // Make a direct request to the resource metadata endpoint
        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(metadata);
        Assert.Equal(McpServerUrl, metadata.Resource);
        Assert.Contains(OAuthServerUrl, metadata.AuthorizationServers);
        Assert.Contains("mcp:basic", metadata.ScopesSupported);
        Assert.Contains("mcp:tools", metadata.ScopesSupported);
        Assert.Equal("Dynamic Test Resource", metadata.ResourceName);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_ThrowsException_WhenNoMetadataProvided()
    {
        // Override the configuration to test the error case where no metadata is provided
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                // Don't set ResourceMetadata and provide an event that doesn't set it either
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Intentionally don't set context.ResourceMetadata to test error handling
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = await StartMcpServerAsync();

        // Make a direct request to the resource metadata endpoint - this should fail
        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        // The request should fail with an internal server error due to the InvalidOperationException
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_HandlesResponse_WhenHandleResponseCalled()
    {
        // Override the configuration to test HandleResponse behavior
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Call HandleResponse() to discontinue processing and return to client
                    context.HandleResponse();
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = await StartMcpServerAsync();

        // Make a direct request to the resource metadata endpoint
        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        // The request should be handled by the event handler without returning metadata
        // Since HandleResponse() was called, the handler should have taken responsibility
        // for generating the response, which in this case means an empty response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The response should be empty since the event handler called HandleResponse()
        // but didn't write any content to the response
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Empty(content);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_SkipsHandler_WhenSkipHandlerCalled()
    {
        // Override the configuration to test SkipHandler behavior
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Call SkipHandler() to discontinue processing in the current handler
                    context.SkipHandler();
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = await StartMcpServerAsync();

        // Make a direct request to the resource metadata endpoint
        using var response = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        // When SkipHandler() is called, the authentication handler should skip processing
        // and let other handlers in the pipeline handle the request. Since there are no
        // other handlers configured for this endpoint, this should result in a 404
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
