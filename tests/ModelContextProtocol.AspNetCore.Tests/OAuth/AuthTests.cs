using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit.Sdk;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public class AuthTests : OAuthTestBase
{
    private const string ClientMetadataDocumentUrl = $"{OAuthServerUrl}/client-metadata/cimd-client.json";

    public AuthTests(ITestOutputHelper outputHelper)
         : base(outputHelper)
    {
    }

    [Fact]
    public async Task CanAuthenticate()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuthorizationCallbackHandler_ReceivesConfiguredRedirectUri()
    {
        await using var app = await StartMcpServerAsync();

        var redirectUri = new Uri("http://localhost:1179/callback");
        AuthorizationCallbackContext? callbackContext = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = redirectUri,
                AuthorizationCallbackHandler = (context, cancellationToken) =>
                {
                    callbackContext = context;
                    return HandleAuthorizationUrlAsync(context, cancellationToken);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(callbackContext);
        Assert.Equal(redirectUri, callbackContext.RedirectUri);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "https://localhost:7029")]
    [InlineData(true, "https://attacker.example")]
    public async Task AuthorizationRedirectDelegate_ReceivesConfiguredUrisAndSkipsResponseIssuerValidation(
        bool authorizationResponseIssParameterSupported,
        string? authorizationResponseIssuer)
    {
        TestOAuthServer.AuthorizationResponseIssParameterSupported = authorizationResponseIssParameterSupported;
        TestOAuthServer.AuthorizationResponseIssuer = authorizationResponseIssuer;
        await using var app = await StartMcpServerAsync();

        var redirectUri = new Uri("http://localhost:1179/callback");
        Uri? receivedAuthorizationUri = null;
        Uri? receivedRedirectUri = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = redirectUri,
#pragma warning disable MCP9007 // Verify the obsolete callback remains functional during its compatibility window.
                AuthorizationRedirectDelegate = (authorizationUri, callbackRedirectUri, cancellationToken) =>
                {
                    receivedAuthorizationUri = authorizationUri;
                    receivedRedirectUri = callbackRedirectUri;
                    return HandleAuthorizationUrlAsync(authorizationUri, callbackRedirectUri, cancellationToken);
                },
#pragma warning restore MCP9007
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(receivedAuthorizationUri);
        Assert.Equal(redirectUri, receivedRedirectUri);
    }

    [Fact]
    public async Task AuthorizationRedirectDelegate_DoesNotSkipMetadataIssuerValidation()
    {
        TestOAuthServer.MetadataIssuerOverride = "https://attacker.example";
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
#pragma warning disable MCP9007 // Verify the obsolete callback retains metadata issuer validation.
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
#pragma warning restore MCP9007
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("does not match the expected issuer", ex.Message);
    }

    [Fact]
    public void HttpClientTransport_RejectsBothAuthorizationCallbacks()
    {
#pragma warning disable MCP9007 // Verify ambiguous legacy and current callback configuration is rejected.
        var options = new ClientOAuthOptions
        {
            RedirectUri = new Uri("http://localhost:1179/callback"),
            AuthorizationCallbackHandler = (_, _) => Task.FromResult<ModelContextProtocol.Authentication.AuthorizationResult?>(new()),
            AuthorizationRedirectDelegate = (_, _, _) => Task.FromResult<string?>("code"),
        };
#pragma warning restore MCP9007

        var ex = Assert.Throws<ArgumentException>(() => new HttpClientTransport(
            new()
            {
                Endpoint = new(McpServerUrl),
                OAuth = options,
            },
            HttpClient,
            LoggerFactory));

        Assert.Contains(nameof(ClientOAuthOptions.AuthorizationCallbackHandler), ex.Message);
#pragma warning disable MCP9007 // The obsolete property name should be included in the diagnostic.
        Assert.Contains(nameof(ClientOAuthOptions.AuthorizationRedirectDelegate), ex.Message);
#pragma warning restore MCP9007
    }

    [Fact]
    public async Task CannotAuthenticate_WithoutOAuthConfiguration()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
        }, HttpClient, LoggerFactory);

        var httpEx = await Assert.ThrowsAsync<HttpRequestException>(async () => await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, httpEx.StatusCode);
    }

    [Fact]
    public async Task CannotAuthenticate_WithUnregisteredClient()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "unregistered-demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // The EqualException is thrown by HandleAuthorizationUrlAsync when the /authorize request gets a 400
        var equalEx = await Assert.ThrowsAsync<EqualException>(async () => await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAuthenticate_WithDynamicClientRegistration()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("native", TestOAuthServer.LastApplicationType);
    }

    [Fact]
    public async Task DynamicClientRegistration_UsesExplicitApplicationType()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new()
                {
                    ApplicationType = "web",
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("web", TestOAuthServer.LastApplicationType);
    }

    [Fact]
    public async Task CanAuthenticate_WithClientMetadataDocument()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri(ClientMetadataDocumentUrl),
                DynamicClientRegistration = new()
                {
                    ApplicationType = "web",
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenMetadataOmitsPkceMethods()
    {
        TestOAuthServer.CodeChallengeMethodsSupported = null;
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        // No discovery endpoint advertises PKCE, so metadata discovery is exhausted. The precise PKCE reason
        // is logged as each endpoint is skipped.
        Assert.Contains(
            MockLoggerProvider.LogMessages,
            m => m.Exception?.Message.Contains("code_challenge_methods_supported") == true);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenMetadataLacksS256PkceMethod()
    {
        TestOAuthServer.CodeChallengeMethodsSupported = ["plain"];
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(
            MockLoggerProvider.LogMessages,
            m => m.Exception?.Message.Contains("required PKCE method 'S256'") == true);
    }

    [Fact]
    public async Task CanAuthenticate_WhenFirstMetadataEndpointOmitsPkce_ButAnotherAdvertisesIt()
    {
        // The OAuth 2.0 authorization server metadata endpoint is tried before the OpenID Connect one.
        // Simulate a server where only the OpenID Connect document advertises PKCE support, and verify the
        // client falls through to it rather than failing on the first PKCE-less document.
        TestOAuthServer.MetadataPathsWithoutPkceSupport.Add("/.well-known/oauth-authorization-server");
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UsesDynamicClientRegistration_WhenCimdNotSupported()
    {
        // Disable CIMD support on the test OAuth server so the client
        // falls back to dynamic registration even if a CIMD URL is provided.
        TestOAuthServer.ClientIdMetadataDocumentSupported = false;

        await using var app = await StartMcpServerAsync();

        // Provide an invalid CIMD URL; if CIMD were used, auth would fail.
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri("http://invalid-cimd.example.com"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client (No CIMD)",
                    ClientUri = new Uri("https://example.com/no-cimd"),
                },
            },
        }, HttpClient, LoggerFactory);

        // Should succeed via dynamic client registration.
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DoesNotUseClientMetadataDocument_WhenClientIdIsSpecified()
    {
        await using var app = await StartMcpServerAsync();

        // Provide an invalid CIMD URL; if CIMD were used, auth would fail.
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri("http://invalid-cimd.example.com"),
                DynamicClientRegistration = new()
                {
                    ApplicationType = "web",
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("http://localhost:7029/client-metadata/cimd-client.json")] // Non-HTTPS Scheme
    [InlineData("http://localhost:7029")] // Missing path
    public async Task CannotAuthenticate_WithInvalidClientMetadataDocument(string uri)
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri(uri),
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.StartsWith("Failed to handle unauthorized response", ex.Message);
    }

    [Fact]
    public async Task CanAuthenticate_WithTokenRefresh()
    {
        var hasForcedRefresh = false;

        Builder.Services.AddMcpServer(options =>
        {
            options.ToolCollection = new();
        });

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            // Add middleware to intercept list tools requests and force a token refresh on the first call
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/" && !hasForcedRefresh)
                {
                    // Enable buffering so we can read the request body multiple times
                    context.Request.EnableBuffering();

                    // Read the request body to check if it's calling tools/list
                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    // Reset the request body position so MapMcp can read it
                    context.Request.Body.Position = 0;

                    // Check if this is a tools/list request
                    if (message is JsonRpcRequest request && request.Method == "tools/list")
                    {
                        hasForcedRefresh = true;

                        // Return 401 to force token refresh
                        await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                        await context.Response.StartAsync(context.RequestAborted);
                        await context.Response.Body.FlushAsync(context.RequestAborted);
                        return; // Short-circuit, don't call next()
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(TestOAuthServer.HasRefreshedToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithExtraParams()
    {
        await using var app = await StartMcpServerAsync();

        Uri? lastAuthorizationUri = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    lastAuthorizationUri = context.AuthorizationUri;
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                AdditionalAuthorizationParameters = new Dictionary<string, string>
                {
                    ["custom_param"] = "custom_value",
                }
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(lastAuthorizationUri?.Query);
        Assert.Contains("custom_param=custom_value", lastAuthorizationUri?.Query);
    }

    [Fact]
    public async Task CannotOverrideExistingParameters_WithExtraParams()
    {
        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                AdditionalAuthorizationParameters = new Dictionary<string, string>
                {
                    ["redirect_uri"] = "custom_value",
                }
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<ArgumentException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAuthenticate_WithoutResourceInWwwAuthenticateHeader()
    {
        await using var app = await StartMcpServerAsync(authScheme: JwtBearerDefaults.AuthenticationScheme);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithoutResourceInWwwAuthenticateHeader_WithPathSuffix()
    {
        const string serverPath = "/mcp";
        await using var app = await StartMcpServerAsync(serverPath, authScheme: JwtBearerDefaults.AuthenticationScheme);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri($"{McpServerUrl}{serverPath}"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AuthorizationFlow_UsesScopeFromProtectedResourceMetadata()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.ScopesSupported = ["mcp:tools", "files:read"];
        });

        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        var requestedScopeSet = new HashSet<string>(requestedScope!.Split(' '));
        Assert.Contains("mcp:tools", requestedScopeSet);
        Assert.Contains("files:read", requestedScopeSet);
    }

    [Fact]
    public async Task AuthorizationFlow_UsesScopeFromChallengeHeader()
    {
        var challengeScopes = "challenge:read challenge:write";

        await using var app = Builder.Build();
        app.Use(next =>
        {
            return async context =>
            {
                await next(context);

                if (context.Response.StatusCode != 401)
                {
                    return;
                }

                context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"{challengeScopes}\"";
            };
        });
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMcp().RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(challengeScopes, requestedScope);
    }

    [Fact]
    public async Task AuthorizationFlow_UsesScopeFromForbiddenHeader()
    {
        var adminScopes = "admin:read admin:write";

        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "admin-tool")]
                (ClaimsPrincipal user) =>
                {
                    // Verify the user's scope claim contains all required admin scopes.
                    // With scope accumulation (SEP-2350), the token scope will be the union
                    // of previously granted and newly challenged scopes.
                    var scopeClaim = user.FindFirst("scope")?.Value ?? "";
                    var scopeSet = new HashSet<string>(scopeClaim.Split(' '));
                    Assert.Contains("admin:read", scopeSet);
                    Assert.Contains("admin:write", scopeSet);
                    return "Admin tool executed.";
                }),
            ]);

        string? requestedScope = null;

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            // Add middleware to intercept requests and check for admin-tool calls
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/")
                {
                    // Enable buffering so we can read the request body multiple times
                    context.Request.EnableBuffering();

                    // Read the request body to check if it's calling admin-tool
                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    // Reset the request body position so MapMcp can read it
                    context.Request.Body.Position = 0;

                    // Check if this is a tools/call request for admin-tool
                    if (message is JsonRpcRequest request && request.Method == "tools/call")
                    {
                        var toolCallParams = JsonSerializer.Deserialize(
                            request.Params,
                            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                        if (toolCallParams?.Name == "admin-tool")
                        {
                            // Check if user has required scopes (scope claim contains all admin scopes)
                            var user = context.User;
                            var scopeClaim = user.FindFirst("scope")?.Value ?? "";
                            var scopeSet = new HashSet<string>(scopeClaim.Split(' '));
                            if (!scopeSet.Contains("admin:read") || !scopeSet.Contains("admin:write"))
                            {
                                // User lacks required scopes, return 403 before MapMcp processes the request
                                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                                context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"{adminScopes}\"";
                                await context.Response.StartAsync(context.RequestAborted);
                                await context.Response.Body.FlushAsync(context.RequestAborted);
                                return; // Short-circuit, don't call next()
                            }
                        }
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mcp:tools", requestedScope);

        var adminResult = await client.CallToolAsync("admin-tool", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Admin tool executed.", adminResult.Content[0].ToString());

        // SEP-2350: Verify that the step-up authorization request includes the union
        // of previously requested scopes (mcp:tools) and newly challenged scopes (admin:read admin:write).
        var requestedScopeSet = new HashSet<string>(requestedScope!.Split(' '));
        Assert.Contains("mcp:tools", requestedScopeSet);
        Assert.Contains("admin:read", requestedScopeSet);
        Assert.Contains("admin:write", requestedScopeSet);
    }

    [Fact]
    public async Task AuthorizationFlow_AccumulatesScopesAcrossMultipleStepUps()
    {
        // SEP-2350: Verify scope accumulation across multiple step-up authorization challenges.
        // First call requires "files:read", second call requires "files:write".
        // The second authorization request should include both "mcp:tools files:read files:write".

        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "read-tool")]
                (ClaimsPrincipal user) =>
                {
                    return "Read tool executed.";
                }),
                McpServerTool.Create([McpServerTool(Name = "write-tool")]
                (ClaimsPrincipal user) =>
                {
                    return "Write tool executed.";
                }),
            ]);

        List<string?> requestedScopes = [];

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/")
                {
                    context.Request.EnableBuffering();

                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    context.Request.Body.Position = 0;

                    if (message is JsonRpcRequest request && request.Method == "tools/call")
                    {
                        var toolCallParams = JsonSerializer.Deserialize(
                            request.Params,
                            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                        var user = context.User;
                        var scopeClaim = user.FindFirst("scope")?.Value ?? "";
                        var scopeSet = new HashSet<string>(scopeClaim.Split(' '));

                        if (toolCallParams?.Name == "read-tool" && !scopeSet.Contains("files:read"))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"files:read\"";
                            await context.Response.StartAsync(context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            return;
                        }

                        if (toolCallParams?.Name == "write-tool" && !scopeSet.Contains("files:write"))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"files:write\"";
                            await context.Response.StartAsync(context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            return;
                        }
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScopes.Add(query["scope"].ToString());
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Initial auth gets "mcp:tools" from protected resource metadata
        Assert.Single(requestedScopes);
        Assert.Equal("mcp:tools", requestedScopes[0]);

        // First step-up: read-tool requires "files:read"
        var readResult = await client.CallToolAsync("read-tool", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Read tool executed.", readResult.Content[0].ToString());
        Assert.Equal(2, requestedScopes.Count);
        var secondScopeSet = new HashSet<string>(requestedScopes[1]!.Split(' '));
        Assert.Contains("mcp:tools", secondScopeSet);
        Assert.Contains("files:read", secondScopeSet);

        // Second step-up: write-tool requires "files:write"
        var writeResult = await client.CallToolAsync("write-tool", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Write tool executed.", writeResult.Content[0].ToString());
        Assert.Equal(3, requestedScopes.Count);
        var thirdScopeSet = new HashSet<string>(requestedScopes[2]!.Split(' '));
        Assert.Contains("mcp:tools", thirdScopeSet);
        Assert.Contains("files:read", thirdScopeSet);
        Assert.Contains("files:write", thirdScopeSet);
    }

    [Fact]
    public async Task AuthorizationFlow_ConcurrentStepUps_ReuseSteppedUpToken_WhenChallengeAddsNoNewScope()
    {
        // Two concurrent calls to the same tool both receive the same insufficient_scope challenge
        // before either has stepped up. They serialize on the provider's token acquisition lock: the
        // first runs the step-up and caches the broader token, and the second must reuse that token
        // instead of failing as a "repeated" challenge. Only one interactive step-up should run.
        TestOAuthServer.AuthorizationResponseIssParameterSupported = true;
        TestOAuthServer.AuthorizationResponseIssuer = OAuthServerUrl;

        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "read-tool")]
                (ClaimsPrincipal user) =>
                {
                    return "Read tool executed.";
                }),
            ]);

        List<string?> requestedScopes = [];
        var scopeLock = new object();

        // Release both initial challenges only after both concurrent calls have reached the server, so
        // the second caller is guaranteed to be waiting on the token lock while the first steps up.
        var bothChallengesReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int challengesReached = 0;

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/")
                {
                    context.Request.EnableBuffering();

                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    context.Request.Body.Position = 0;

                    if (message is JsonRpcRequest request && request.Method == "tools/call")
                    {
                        var toolCallParams = JsonSerializer.Deserialize(
                            request.Params,
                            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                        var user = context.User;
                        var scopeClaim = user.FindFirst("scope")?.Value ?? "";
                        var scopeSet = new HashSet<string>(scopeClaim.Split(' '));

                        if (toolCallParams?.Name == "read-tool" && !scopeSet.Contains("files:read"))
                        {
                            if (Interlocked.Increment(ref challengesReached) == 2)
                            {
                                bothChallengesReached.TrySetResult();
                            }

                            await bothChallengesReached.Task.WaitAsync(TestConstants.DefaultTimeout, context.RequestAborted);

                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"files:read\"";
                            await context.Response.StartAsync(context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            return;
                        }
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, cancellationToken) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    lock (scopeLock)
                    {
                        requestedScopes.Add(query["scope"].ToString());
                    }
                    return HandleAuthorizationUrlAsync(context, cancellationToken);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Initial connect requests "mcp:tools" from protected resource metadata.
        Assert.Single(requestedScopes);
        Assert.Equal("mcp:tools", requestedScopes[0]);

        var firstCall = client.CallToolAsync("read-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask();
        var secondCall = client.CallToolAsync("read-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask();

        var results = await Task.WhenAll(firstCall, secondCall);

        Assert.Equal("Read tool executed.", results[0].Content[0].ToString());
        Assert.Equal("Read tool executed.", results[1].Content[0].ToString());

        // Only one interactive step-up should have run; the second caller reused the token from the first.
        Assert.Equal(2, requestedScopes.Count);
        var stepUpScopes = new HashSet<string>(requestedScopes[1]!.Split(' '));
        Assert.Contains("mcp:tools", stepUpScopes);
        Assert.Contains("files:read", stepUpScopes);
    }

    [Fact]
    public async Task AuthorizationFlow_StopsSteppingUpWhenChallengeAddsNoNewScope()
    {
        // SEP-2350: A misconfigured server repeats the same insufficient_scope challenge even after the
        // client has already requested that scope. Re-running interactive authorization cannot make
        // progress, so the client must treat it as a permanent failure rather than prompting the user
        // again on every call to the same resource and operation.

        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "deny-tool")]
                (ClaimsPrincipal user) =>
                {
                    return "Deny tool executed.";
                }),
            ]);

        List<string?> requestedScopes = [];

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/")
                {
                    context.Request.EnableBuffering();

                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    context.Request.Body.Position = 0;

                    if (message is JsonRpcRequest request && request.Method == "tools/call")
                    {
                        var toolCallParams = JsonSerializer.Deserialize(
                            request.Params,
                            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                        // Always reject "deny-tool" with the same challenge, regardless of the token's scopes.
                        if (toolCallParams?.Name == "deny-tool")
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"files:read\"";
                            await context.Response.StartAsync(context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            return;
                        }
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScopes.Add(query["scope"].ToString());
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Initial auth gets "mcp:tools" from protected resource metadata.
        Assert.Single(requestedScopes);

        // First call introduces a new scope ("files:read"), so exactly one step-up authorization occurs.
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.CallToolAsync("deny-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(2, requestedScopes.Count);

        // Second call repeats the same challenge with no new scope. The client must NOT prompt again;
        // it surfaces a permanent authorization failure instead of re-running interactive authorization.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => client.CallToolAsync("deny-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("added no scope beyond those already requested", ex.ToString());

        // No additional authorization prompt was triggered by the second call.
        Assert.Equal(2, requestedScopes.Count);
    }

    [Fact]
    public async Task AuthorizationFlow_AllowsOneStepUpEvenWhenChallengeAddsNoNewScope()
    {
        // SEP-2350 (strict reading): A step-up authorization is always allowed at least once, even when
        // the challenged scope was already requested during the initial authorization. Only a *repeated*
        // challenge that still adds no new scope is treated as permanent. Here the server always rejects
        // "deny-tool" with the same "mcp:tools" scope that the client already requested on initial connect.

        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "deny-tool")]
                (ClaimsPrincipal user) =>
                {
                    return "Deny tool executed.";
                }),
            ]);

        List<string?> requestedScopes = [];

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == HttpMethods.Post && context.Request.Path == "/")
                {
                    context.Request.EnableBuffering();

                    var message = await JsonSerializer.DeserializeAsync(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage)),
                        context.RequestAborted) as JsonRpcMessage;

                    context.Request.Body.Position = 0;

                    if (message is JsonRpcRequest request && request.Method == "tools/call")
                    {
                        var toolCallParams = JsonSerializer.Deserialize(
                            request.Params,
                            McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(CallToolRequestParams))) as CallToolRequestParams;

                        // Always reject "deny-tool" challenging "mcp:tools", which the client already
                        // requested on initial connect, so the challenge never introduces a new scope.
                        if (toolCallParams?.Name == "deny-tool")
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"mcp:tools\"";
                            await context.Response.StartAsync(context.RequestAborted);
                            await context.Response.Body.FlushAsync(context.RequestAborted);
                            return;
                        }
                    }
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScopes.Add(query["scope"].ToString());
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Initial auth already requests "mcp:tools" from protected resource metadata.
        Assert.Single(requestedScopes);
        Assert.Equal("mcp:tools", requestedScopes[0]);

        // First call: even though the challenged scope is not new, one step-up attempt is still allowed,
        // so a second authorization request is made.
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.CallToolAsync("deny-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(2, requestedScopes.Count);

        // Second call: the step-up has already been attempted and the challenge still adds no new scope,
        // so the client surfaces a permanent failure without prompting again.
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => client.CallToolAsync("deny-tool", cancellationToken: TestContext.Current.CancellationToken).AsTask());
        Assert.Contains("added no scope beyond those already requested", ex.ToString());
        Assert.Equal(2, requestedScopes.Count);
    }

    [Fact]
    public async Task AuthorizationFails_WhenResourceMetadataPortDiffers()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.Resource = "http://localhost:5999";
        });

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CannotAuthenticate_WhenProtectedResourceMetadataMissingResource()
    {
        TestOAuthServer.ExpectResource = false;

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnResourceMetadataRequest = async context =>
            {
                context.HandleResponse();

                var metadata = new ProtectedResourceMetadata
                {
                    AuthorizationServers = { OAuthServerUrl },
                    ScopesSupported = ["mcp:tools"],
                };

                await Results.Json(metadata, McpJsonUtilities.DefaultOptions).ExecuteAsync(context.HttpContext);
            };
        });

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Resource URI in metadata", ex.Message);
    }

    [Fact]
    public async Task CanAuthenticate_WithAuthorizationServerPathInsertionMetadata()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.AuthorizationServers = [$"{OAuthServerUrl}/tenant1"];
        });

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        var requests = TestOAuthServer.MetadataRequests.ToArray();
        Assert.Contains("/.well-known/oauth-authorization-server/tenant1", requests);
    }

    [Fact]
    public async Task CanAuthenticate_WithAuthorizationServerPathFallbacks()
    {
        const string issuerPath = "/subdir/tenant2";
        TestOAuthServer.DisabledMetadataPaths.Add($"/.well-known/oauth-authorization-server{issuerPath}");
        TestOAuthServer.DisabledMetadataPaths.Add($"/.well-known/openid-configuration{issuerPath}");

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.AuthorizationServers = [$"{OAuthServerUrl}{issuerPath}"];
        });

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                $"/.well-known/oauth-authorization-server{issuerPath}",
                $"/.well-known/openid-configuration{issuerPath}",
                $"{issuerPath}/.well-known/openid-configuration",
                "/.well-known/openid-configuration",
            ],
            TestOAuthServer.MetadataRequests);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenAuthorizationServerMetadataIssuerMismatches()
    {
        TestOAuthServer.MetadataIssuerOverride = "https://attacker.example";

        await using var app = await StartMcpServerAsync();
        await using var transport = CreateOAuthTransport();

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("does not match the expected issuer", ex.Message);
        Assert.Single(TestOAuthServer.MetadataRequests);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenAuthorizationServerMetadataOmitsIssuer()
    {
        TestOAuthServer.IncludeIssuerInMetadata = false;

        await using var app = await StartMcpServerAsync();
        await using var transport = CreateOAuthTransport();

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("did not provide the required issuer", ex.Message);
        Assert.Single(TestOAuthServer.MetadataRequests);
    }

    [Fact]
    public async Task CanAuthenticate_WhenAuthorizationResponseIssuerMatches()
    {
        TestOAuthServer.AuthorizationResponseIssParameterSupported = true;
        TestOAuthServer.AuthorizationResponseIssuer = OAuthServerUrl;

        await using var app = await StartMcpServerAsync();
        await using var transport = CreateOAuthTransport();
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, "https://attacker.example", "does not match expected issuer")]
    [InlineData(true, null, "advertises RFC 9207 iss parameter support but none was received")]
    [InlineData(false, "https://attacker.example", "does not match expected issuer")]
    public async Task CannotAuthenticate_WhenAuthorizationResponseIssuerIsInvalid(
        bool authorizationResponseIssParameterSupported,
        string? authorizationResponseIssuer,
        string expectedMessage)
    {
        TestOAuthServer.AuthorizationResponseIssParameterSupported = authorizationResponseIssParameterSupported;
        TestOAuthServer.AuthorizationResponseIssuer = authorizationResponseIssuer;

        await using var app = await StartMcpServerAsync();
        await using var transport = CreateOAuthTransport();

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public async Task CanAuthenticate_WithResourceMetadataPathFallbacks()
    {
        const string resourcePath = "/mcp";
        List<string> wellKnownRequests = [];

        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);
        await using var app = Builder.Build();

        var metadata = new ProtectedResourceMetadata
        {
            Resource = $"{McpServerUrl}{resourcePath}",
            AuthorizationServers = { OAuthServerUrl },
        };

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-protected-resource", out var remaining))
            {
                wellKnownRequests.Add(context.Request.Path);
                if (remaining.HasValue)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
            }

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMcp(resourcePath).RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var endpoint = new Uri(new Uri(McpServerUrl), resourcePath);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = endpoint,
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                $"/.well-known/oauth-protected-resource{resourcePath}",
                "/.well-known/oauth-protected-resource"
            ],
            wellKnownRequests);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenResourceMetadataResourceIsNonRootParentPath()
    {
        const string configuredResourcePath = "/mcp";
        const string requestedResourcePath = "/mcp/tools";

        // Remove resource_metadata from the WWW-Authenticate header, because we should only fall back at all (even to root) when it's missing.
        //
        // If the protected resource metadata was retrieved from a URL returned by the protected resource via the WWW-Authenticate resource_metadata parameter,
        // then the resource value returned MUST be identical to the URL that the client used to make the request to the resource server.
        // If these values are not identical, the data contained in the response MUST NOT be used.
        //
        // https://datatracker.ietf.org/doc/html/rfc9728/#section-3.3
        //
        // CanAuthenticate_WhenWwwAuthenticateResourceMetadataIsRootPath validates that a root-level resource is accepted in this case.
        // CanAuthenticate_WithResourceMetadataPathFallbacks validates we will fall back to root when resource_metadata is missing.
        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = $"{McpServerUrl}{configuredResourcePath}",
                AuthorizationServers = { OAuthServerUrl },
            };
        });

        await using var app = Builder.Build();

        app.MapMcp(requestedResourcePath).RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri($"{McpServerUrl}{requestedResourcePath}"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await McpClient.CreateAsync(
                transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        });

        Assert.Contains("does not match", ex.Message);
    }

    /// <summary>
    /// Verifies that OAuth authentication succeeds when the protected resource metadata URI
    /// matches the root server URL, even when the actual MCP endpoint is at a subpath.
    /// This tests the flexible URI matching behavior where the resource URI can be less specific
    /// than the actual endpoint being accessed.
    /// </summary>
    [Fact]
    public async Task CanAuthenticate_WhenWwwAuthenticateResourceMetadataIsRootPath()
    {
        const string requestedResourcePath = "/mcp/tools";

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = McpServerUrl,
                AuthorizationServers = { OAuthServerUrl },
            };
        });

        await using var app = Builder.Build();

        app.MapMcp(requestedResourcePath).RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri($"{McpServerUrl}{requestedResourcePath}"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that OAuth authentication fails when the protected resource metadata URI
    /// does not match the requested MCP server endpoint. This ensures that clients cannot
    /// use OAuth tokens intended for one server to access a different server.
    /// </summary>
    [Fact]
    public async Task CannotAuthenticate_WhenResourceMetadataUriDoesNotMatch()
    {
        const string requestedResourcePath = "/mcp/tools";
        const string differentResourceUri = "http://different-server.example.com";

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = differentResourceUri,
                AuthorizationServers = { OAuthServerUrl },
            };
        });

        await using var app = Builder.Build();

        app.MapMcp(requestedResourcePath).RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri($"{McpServerUrl}{requestedResourcePath}"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // This should fail because the resource URI doesn't match
        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("does not match", ex.Message);
    }

    /// <summary>
    /// Verifies that OAuth authentication fails when the protected resource metadata URI is an
    /// unrelated path on the same host as the requested endpoint (e.g. resource=.../service-a vs
    /// endpoint .../service-b). This ensures the authority-level fallback only accepts an exact match
    /// or an authority-only resource, and not arbitrary sibling paths on the same host.
    /// </summary>
    [Fact]
    public async Task CannotAuthenticate_WhenResourceMetadataResourceIsDifferentPathOnSameAuthority()
    {
        const string requestedResourcePath = "/service-b";
        const string differentResourcePath = "/service-a";

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = $"{McpServerUrl}{differentResourcePath}",
                AuthorizationServers = { OAuthServerUrl },
            };
        });

        await using var app = Builder.Build();

        app.MapMcp(requestedResourcePath).RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new Uri($"{McpServerUrl}{requestedResourcePath}"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // This should fail because the resource URI is a different path on the same host,
        // which is neither an exact match nor the authority-only base URL.
        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public async Task ResourceMetadata_DoesNotAddTrailingSlash()
    {
        // This test verifies that automatically derived resource URIs don't have trailing slashes
        // and that the client doesn't add them during authentication

        // Don't explicitly set Resource - let it be derived from the request
        await using var app = await StartMcpServerAsync();

        // First, manually check the PRM document doesn't contain a trailing slash
        using var metadataResponse = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(metadata);
        Assert.Equal("http://localhost:5000", metadata.Resource);
        Assert.DoesNotMatch(@"/$", metadata.Resource); // No trailing slash

        // Then authenticate with the client - this will use the derived resource URI
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // This should succeed - the client should not add a trailing slash
        // If the client incorrectly added a trailing slash, ValidResources would reject it
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void CloneResourceMetadataClonesAllProperties()
    {
        var propertyNames = typeof(ProtectedResourceMetadata).GetProperties().Select(property => property.Name).ToList();

        // Set metadata properties to non-default values to verify they're copied.
        var metadata = new ProtectedResourceMetadata
        {
            Resource = "https://example.com/resource",
            AuthorizationServers = ["https://auth1.example.com", "https://auth2.example.com"],
            BearerMethodsSupported = ["header", "body", "query"],
            ScopesSupported = ["read", "write", "admin"],
            JwksUri = "https://example.com/.well-known/jwks.json",
            ResourceSigningAlgValuesSupported = ["RS256", "ES256"],
            ResourceName = "Test Resource",
            ResourceDocumentation = "https://docs.example.com",
            ResourcePolicyUri = "https://example.com/policy",
            ResourceTosUri = "https://example.com/terms",
            TlsClientCertificateBoundAccessTokens = true,
            AuthorizationDetailsTypesSupported = ["payment_initiation", "account_information"],
            DpopSigningAlgValuesSupported = ["RS256", "PS256"],
            DpopBoundAccessTokensRequired = true
        };

        var clonedMetadata = metadata.Clone();

        // Ensure the cloned metadata is not the same instance
        Assert.NotSame(metadata, clonedMetadata);

        // Verify Resource property
        Assert.Equal(metadata.Resource, clonedMetadata.Resource);
        Assert.True(propertyNames.Remove(nameof(metadata.Resource)));

        // Verify AuthorizationServers list is cloned and contains the same values
        Assert.NotSame(metadata.AuthorizationServers, clonedMetadata.AuthorizationServers);
        Assert.Equal(metadata.AuthorizationServers, clonedMetadata.AuthorizationServers);
        Assert.True(propertyNames.Remove(nameof(metadata.AuthorizationServers)));

        // Verify BearerMethodsSupported list is cloned and contains the same values
        Assert.NotSame(metadata.BearerMethodsSupported, clonedMetadata.BearerMethodsSupported);
        Assert.Equal(metadata.BearerMethodsSupported, clonedMetadata.BearerMethodsSupported);
        Assert.True(propertyNames.Remove(nameof(metadata.BearerMethodsSupported)));

        // Verify ScopesSupported list is cloned and contains the same values
        Assert.NotSame(metadata.ScopesSupported, clonedMetadata.ScopesSupported);
        Assert.Equal(metadata.ScopesSupported, clonedMetadata.ScopesSupported);
        Assert.True(propertyNames.Remove(nameof(metadata.ScopesSupported)));

        // Verify JwksUri property
        Assert.Equal(metadata.JwksUri, clonedMetadata.JwksUri);
        Assert.True(propertyNames.Remove(nameof(metadata.JwksUri)));

        // Verify ResourceSigningAlgValuesSupported list is cloned (nullable list)
        Assert.NotSame(metadata.ResourceSigningAlgValuesSupported, clonedMetadata.ResourceSigningAlgValuesSupported);
        Assert.Equal(metadata.ResourceSigningAlgValuesSupported, clonedMetadata.ResourceSigningAlgValuesSupported);
        Assert.True(propertyNames.Remove(nameof(metadata.ResourceSigningAlgValuesSupported)));

        // Verify ResourceName property
        Assert.Equal(metadata.ResourceName, clonedMetadata.ResourceName);
        Assert.True(propertyNames.Remove(nameof(metadata.ResourceName)));

        // Verify ResourceDocumentation property
        Assert.Equal(metadata.ResourceDocumentation, clonedMetadata.ResourceDocumentation);
        Assert.True(propertyNames.Remove(nameof(metadata.ResourceDocumentation)));

        // Verify ResourcePolicyUri property
        Assert.Equal(metadata.ResourcePolicyUri, clonedMetadata.ResourcePolicyUri);
        Assert.True(propertyNames.Remove(nameof(metadata.ResourcePolicyUri)));

        // Verify ResourceTosUri property
        Assert.Equal(metadata.ResourceTosUri, clonedMetadata.ResourceTosUri);
        Assert.True(propertyNames.Remove(nameof(metadata.ResourceTosUri)));

        // Verify TlsClientCertificateBoundAccessTokens property
        Assert.Equal(metadata.TlsClientCertificateBoundAccessTokens, clonedMetadata.TlsClientCertificateBoundAccessTokens);
        Assert.True(propertyNames.Remove(nameof(metadata.TlsClientCertificateBoundAccessTokens)));

        // Verify AuthorizationDetailsTypesSupported list is cloned (nullable list)
        Assert.NotSame(metadata.AuthorizationDetailsTypesSupported, clonedMetadata.AuthorizationDetailsTypesSupported);
        Assert.Equal(metadata.AuthorizationDetailsTypesSupported, clonedMetadata.AuthorizationDetailsTypesSupported);
        Assert.True(propertyNames.Remove(nameof(metadata.AuthorizationDetailsTypesSupported)));

        // Verify DpopSigningAlgValuesSupported list is cloned (nullable list)
        Assert.NotSame(metadata.DpopSigningAlgValuesSupported, clonedMetadata.DpopSigningAlgValuesSupported);
        Assert.Equal(metadata.DpopSigningAlgValuesSupported, clonedMetadata.DpopSigningAlgValuesSupported);
        Assert.True(propertyNames.Remove(nameof(metadata.DpopSigningAlgValuesSupported)));

        // Verify DpopBoundAccessTokensRequired property
        Assert.Equal(metadata.DpopBoundAccessTokensRequired, clonedMetadata.DpopBoundAccessTokensRequired);
        Assert.True(propertyNames.Remove(nameof(metadata.DpopBoundAccessTokensRequired)));

        // Ensure we've checked every property. When new properties get added, we'll have to update this test along with the Clone implementation.
        Assert.Empty(propertyNames);
    }

    [Fact]
    public async Task ResourceMetadata_PreservesExplicitTrailingSlash()
    {
        // This test verifies that explicitly configured trailing slashes are preserved
        const string resourceWithTrailingSlash = "http://localhost:5000/";

        // Configure ValidResources to accept the trailing slash version for this test
        TestOAuthServer.ValidResources = [resourceWithTrailingSlash, "http://localhost:5000/mcp"];

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = resourceWithTrailingSlash,
                AuthorizationServers = { OAuthServerUrl },
                ScopesSupported = ["mcp:tools"],
            };
        });

        await using var app = await StartMcpServerAsync();

        // First, manually check the PRM document contains the trailing slash
        using var metadataResponse = await HttpClient.GetAsync(
            "/.well-known/oauth-protected-resource",
            TestContext.Current.CancellationToken
        );

        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(metadata);
        Assert.Equal(resourceWithTrailingSlash, metadata.Resource);
        Assert.Matches(@"/$", metadata.Resource); // Has trailing slash

        // Then authenticate with the client
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // This should succeed with the explicitly configured trailing slash
        // If the client incorrectly trimmed the slash, ValidResources would reject it
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithLegacyServerWithoutProtectedResourceMetadata()
    {
        // 2025-03-26 backcompat: server does NOT serve PRM, but DOES serve auth server metadata.
        // The client should fall back to using the MCP server's origin as the auth server
        // and discover auth metadata from well-known URLs on that origin.
        TestOAuthServer.ExpectResource = false;

        // Use JwtBearer as the challenge scheme so the 401 response does NOT include resource_metadata.
        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);

        // Legacy servers don't use resource-based audiences in tokens (no resource parameter is sent).
        Builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = false;
        });

        await using var app = Builder.Build();

        // Capture HttpClient for use in the proxy middleware.
        var httpClient = HttpClient;

        app.Use(async (context, next) =>
        {
            // Return 404 for PRM to simulate a legacy server that doesn't support RFC 9728.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-protected-resource"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Serve auth server metadata pointing to the MCP server's own endpoints.
            // In a real 2025-03-26 deployment, the MCP server itself would be the auth server.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-authorization-server") ||
                context.Request.Path.StartsWithSegments("/.well-known/openid-configuration"))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($$"""
                    {
                        "issuer": "{{OAuthServerUrl}}",
                        "authorization_endpoint": "{{McpServerUrl}}/authorize",
                        "token_endpoint": "{{McpServerUrl}}/token",
                        "registration_endpoint": "{{McpServerUrl}}/register",
                        "response_types_supported": ["code"],
                        "grant_types_supported": ["authorization_code", "refresh_token"],
                        "token_endpoint_auth_methods_supported": ["client_secret_post"],
                        "code_challenge_methods_supported": ["S256"]
                    }
                    """);
                return;
            }

            // Proxy OAuth endpoints to the real OAuth server.
            // In a real 2025-03-26 deployment, the MCP server itself would host these endpoints.
            var path = context.Request.Path.Value;
            if (path is "/authorize" or "/token" or "/register")
            {
                var targetUrl = $"{OAuthServerUrl}{path}{context.Request.QueryString}";
                using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

                if (context.Request.ContentLength > 0 || context.Request.ContentType is not null)
                {
                    proxyRequest.Content = new StreamContent(context.Request.Body);
                    if (context.Request.ContentType is not null)
                    {
                        proxyRequest.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                    }
                }

                if (context.Request.Headers.Authorization.Count > 0)
                {
                    proxyRequest.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
                }

                using var response = await httpClient.SendAsync(proxyRequest);
                context.Response.StatusCode = (int)response.StatusCode;

                if (response.Headers.Location is not null)
                {
                    context.Response.Headers.Location = response.Headers.Location.ToString();
                }

                if (response.Content.Headers.ContentType is not null)
                {
                    context.Response.ContentType = response.Content.Headers.ContentType.ToString();
                }

                await response.Content.CopyToAsync(context.Response.Body);
                return;
            }

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp().RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithLegacyServerUsingDefaultEndpointFallback()
    {
        // 2025-03-26 backcompat: server does NOT serve PRM AND does NOT serve auth server metadata.
        // The client should fall back to default endpoint paths (/authorize, /token, /register)
        // on the MCP server's origin.
        TestOAuthServer.ExpectResource = false;

        // Use JwtBearer as the challenge scheme so the 401 response does NOT include resource_metadata.
        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);

        // Legacy servers don't use resource-based audiences in tokens (no resource parameter is sent).
        Builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = false;
        });

        await using var app = Builder.Build();

        // Capture HttpClient for use in the proxy middleware.
        var httpClient = HttpClient;

        app.Use(async (context, next) =>
        {
            // Return 404 for PRM to simulate a legacy server that doesn't support RFC 9728.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-protected-resource"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Return 404 for auth server metadata to force fallback to default endpoints.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-authorization-server") ||
                context.Request.Path.StartsWithSegments("/.well-known/openid-configuration"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Proxy default OAuth endpoints to the real OAuth server.
            // In a real 2025-03-26 deployment, the MCP server itself would host these endpoints.
            var path = context.Request.Path.Value;
            if (path is "/authorize" or "/token" or "/register")
            {
                var targetUrl = $"{OAuthServerUrl}{path}{context.Request.QueryString}";
                using var proxyRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

                if (context.Request.ContentLength > 0 || context.Request.ContentType is not null)
                {
                    proxyRequest.Content = new StreamContent(context.Request.Body);
                    if (context.Request.ContentType is not null)
                    {
                        proxyRequest.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
                    }
                }

                if (context.Request.Headers.Authorization.Count > 0)
                {
                    proxyRequest.Headers.TryAddWithoutValidation("Authorization", context.Request.Headers.Authorization.ToString());
                }

                using var response = await httpClient.SendAsync(proxyRequest);
                context.Response.StatusCode = (int)response.StatusCode;

                if (response.Headers.Location is not null)
                {
                    context.Response.Headers.Location = response.Headers.Location.ToString();
                }

                if (response.Content.Headers.ContentType is not null)
                {
                    context.Response.ContentType = response.Content.Headers.ContentType.ToString();
                }

                await response.Content.CopyToAsync(context.Response.Body);
                return;
            }

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp().RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CannotAuthenticate_WithLegacyServerWhoseMetadataOmitsPkceMethods()
    {
        // 2025-03-26 backcompat regression guard: PRM is unavailable (resourceUri is null), but the server
        // DOES serve an auth server metadata document that omits 'code_challenge_methods_supported'.
        // The client must refuse to proceed rather than falling back to synthesized S256 defaults, since a
        // discovered metadata document that fails PKCE validation disqualifies the legacy default fallback.
        TestOAuthServer.ExpectResource = false;

        // Use JwtBearer as the challenge scheme so the 401 response does NOT include resource_metadata.
        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);

        Builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.TokenValidationParameters.ValidateAudience = false;
        });

        await using var app = Builder.Build();

        app.Use(async (context, next) =>
        {
            // Return 404 for PRM to simulate a legacy server that doesn't support RFC 9728.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-protected-resource"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Serve auth server metadata that omits 'code_challenge_methods_supported' entirely.
            if (context.Request.Path.StartsWithSegments("/.well-known/oauth-authorization-server") ||
                context.Request.Path.StartsWithSegments("/.well-known/openid-configuration"))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($$"""
                    {
                        "issuer": "{{OAuthServerUrl}}",
                        "authorization_endpoint": "{{McpServerUrl}}/authorize",
                        "token_endpoint": "{{McpServerUrl}}/token",
                        "registration_endpoint": "{{McpServerUrl}}/register",
                        "response_types_supported": ["code"],
                        "grant_types_supported": ["authorization_code", "refresh_token"],
                        "token_endpoint_auth_methods_supported": ["client_secret_post"]
                    }
                    """);
                return;
            }

            await next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcp().RequireAuthorization();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        // The specific PKCE failure reason is surfaced rather than a generic discovery failure or a
        // silently-synthesized S256 fallback.
        Assert.Contains("code_challenge_methods_supported", ex.Message);
    }

    [Fact]
    public async Task AuthorizationFlow_AppendsOfflineAccess_WhenServerAdvertisesIt()
    {
        TestOAuthServer.IncludeOfflineAccessInMetadata = true;
        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(requestedScope);
        Assert.Contains("offline_access", requestedScope!.Split(' '));
    }

    [Fact]
    public async Task AuthorizationFlow_DoesNotAppendOfflineAccess_WhenServerDoesNotAdvertiseIt()
    {
        // IncludeOfflineAccessInMetadata defaults to false, so the AS will not advertise offline_access.
        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(requestedScope);
        Assert.DoesNotContain("offline_access", requestedScope!.Split(' '));
    }

    [Fact]
    public async Task AuthorizationFlow_DoesNotDuplicateOfflineAccess_WhenAlreadyPresent()
    {
        TestOAuthServer.IncludeOfflineAccessInMetadata = true;

        // Configure the PRM to already include offline_access in its scopes.
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.ScopesSupported = ["mcp:tools", "offline_access"];
        });

        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(requestedScope);
        var scopeTokens = requestedScope!.Split(' ');
        Assert.Single(scopeTokens, t => t == "offline_access");
    }

    [Fact]
    public async Task AuthorizationFlow_ScopeSelector_CanFilterServerProposedScopes()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.ScopesSupported = ["mcp:tools", "files:read"];
        });

        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                ScopeSelector = scopes => scopes?.Where(s => s == "mcp:tools"),
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mcp:tools", requestedScope);
    }

    [Fact]
    public async Task AuthorizationFlow_ScopeSelector_CanAddCustomScope()
    {
        await using var app = await StartMcpServerAsync();

        string? requestedScope = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(context.AuthorizationUri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                ScopeSelector = scopes => scopes?.Append("custom:scope") ?? ["custom:scope"],
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(requestedScope);
        Assert.Contains("custom:scope", requestedScope!.Split(' '));
    }

    [Fact]
    public async Task AuthorizationFlow_ScopeSelector_ReceivesNull_WhenServerProvidesNoScopes()
    {
        // No ScopesSupported on PRM, no Scopes fallback on client, no offline_access on AS (default).
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.ScopesSupported = [];
        });

        await using var app = await StartMcpServerAsync();

        IEnumerable<string>? capturedInput = ["sentinel"];

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                ScopeSelector = scopes =>
                {
                    capturedInput = scopes;
                    return scopes;
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(capturedInput);
    }

    [Fact]
    public async Task AuthorizationFlow_ScopeSelector_ReturningNull_OmitsScopeParameter()
    {
        await using var app = await StartMcpServerAsync();

        bool? scopePresent = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    scopePresent = QueryHelpers.ParseQuery(context.AuthorizationUri.Query).ContainsKey("scope");
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                ScopeSelector = _ => null,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(scopePresent);
    }

    [Fact]
    public async Task AuthorizationFlow_ScopeSelector_ReturningEmpty_OmitsScopeParameter()
    {
        await using var app = await StartMcpServerAsync();

        bool? scopePresent = null;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (context, ct) =>
                {
                    scopePresent = QueryHelpers.ParseQuery(context.AuthorizationUri.Query).ContainsKey("scope");
                    return HandleAuthorizationUrlAsync(context, ct);
                },
                ScopeSelector = _ => [],
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(scopePresent);
    }

    private HttpClientTransport CreateOAuthTransport() =>
        new(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

    [Fact]
    public async Task DynamicClientRegistration_ScopeSelector_AppliesToDcrScope()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.ScopesSupported = ["mcp:tools", "files:read"];
        });

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new() { ClientName = "Test MCP Client" },
                ScopeSelector = scopes => scopes?.Where(s => s == "mcp:tools"),
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mcp:tools", TestOAuthServer.LastRegistrationScope);
    }
}
