using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for MCP authentication when resource metadata is provided via events rather than static configuration.
/// </summary>
public class AuthEventTests : KestrelInMemoryTest, IAsyncDisposable
{
    private const string McpServerUrl = "http://localhost:5000";
    private const string OAuthServerUrl = "https://localhost:7029";

    private readonly CancellationTokenSource _testCts = new();
    private readonly TestOAuthServer.Program _testOAuthServer;
    private readonly Task _testOAuthRunTask;

    public AuthEventTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        // Let the HandleAuthorizationUrlAsync take a look at the Location header
        SocketsHttpHandler.AllowAutoRedirect = false;
        // The dev cert may not be installed on the CI, but AddJwtBearer requires an HTTPS backchannel by default.
        // The easiest workaround is to disable cert validation for testing purposes.
        SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _testOAuthServer = new TestOAuthServer.Program(
            XunitLoggerProvider,
            KestrelInMemoryTransport
        );
        _testOAuthRunTask = _testOAuthServer.RunServerAsync(cancellationToken: _testCts.Token);

        Builder
            .Services.AddAuthentication(options =>
            {
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Backchannel = HttpClient;
                options.Authority = OAuthServerUrl;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidAudience = McpServerUrl,
                    ValidIssuer = OAuthServerUrl,
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                };
            })
            .AddMcp(options =>
            {
                // Note: ResourceMetadata is NOT set here - it will be provided via events
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Dynamically provide the resource metadata
                    context.ResourceMetadata = new ProtectedResourceMetadata
                    {
                        Resource = new Uri(McpServerUrl),
                        AuthorizationServers = { new Uri(OAuthServerUrl) },
                        ScopesSupported = ["mcp:tools"],
                    };
                    await Task.CompletedTask;
                };
            });

        Builder.Services.AddAuthorization();
    }

    public async ValueTask DisposeAsync()
    {
        _testCts.Cancel();
        try
        {
            await _testOAuthRunTask;
        }
        catch (OperationCanceledException) { }
        finally
        {
            _testCts.Dispose();
        }
    }

    [Fact]
    public async Task CanAuthenticate_WithResourceMetadataFromEvent()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Assert.Equal(new Uri(McpServerUrl), metadata.Resource);
        Assert.Contains(new Uri(OAuthServerUrl), metadata.AuthorizationServers);
        Assert.Contains("mcp:tools", metadata.ScopesSupported);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_CanModifyExistingMetadata_InEvent()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        // Override the configuration to test modification of existing metadata
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                // Set initial metadata
                options.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = new Uri(McpServerUrl),
                    AuthorizationServers = { new Uri(OAuthServerUrl) },
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

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Assert.Equal(new Uri(McpServerUrl), metadata.Resource);
        Assert.Contains(new Uri(OAuthServerUrl), metadata.AuthorizationServers);
        Assert.Contains("mcp:basic", metadata.ScopesSupported);
        Assert.Contains("mcp:tools", metadata.ScopesSupported);
        Assert.Equal("Dynamic Test Resource", metadata.ResourceName);
    }

    [Fact]
    public async Task ResourceMetadataEndpoint_ThrowsException_WhenNoMetadataProvided()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        // Override the configuration to test the error case where no metadata is provided
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                // Don't set ResourceMetadata and provide an event that doesn't set it either
                options.ResourceMetadata = null;
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Intentionally don't set context.ResourceMetadata to test error handling
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        // Override the configuration to test HandleResponse behavior
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.ResourceMetadata = null;
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Call HandleResponse() to discontinue processing and return to client
                    context.HandleResponse();
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        // Override the configuration to test SkipHandler behavior
        Builder.Services.Configure<McpAuthenticationOptions>(
            McpAuthenticationDefaults.AuthenticationScheme,
            options =>
            {
                options.ResourceMetadata = null;
                options.Events.OnResourceMetadataRequest = async context =>
                {
                    // Call SkipHandler() to discontinue processing in the current handler
                    context.SkipHandler();
                    await Task.CompletedTask;
                };
            }
        );

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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

    private async Task<string?> HandleAuthorizationUrlAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken
    )
    {
        using var redirectResponse = await HttpClient.GetAsync(authorizationUri, cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, redirectResponse.StatusCode);
        var location = redirectResponse.Headers.Location;

        if (location is not null && !string.IsNullOrEmpty(location.Query))
        {
            var queryParams = QueryHelpers.ParseQuery(location.Query);
            return queryParams["code"];
        }

        return null;
    }
}
