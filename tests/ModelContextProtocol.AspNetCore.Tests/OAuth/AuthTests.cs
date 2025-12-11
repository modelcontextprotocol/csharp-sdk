using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using System.Net;
using System.Reflection;
using System.Security.Claims;
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ClientUri = new Uri("https://example.com"),
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri(ClientMetadataDocumentUrl)
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                ClientMetadataDocumentUri = new Uri("http://invalid-cimd.example.com"),
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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

        Builder.Services.AddHttpContextAccessor();
        Builder.Services.AddMcpServer(options =>
            {
                options.ToolCollection = new();
            })
            .AddListToolsFilter(next =>
            {
                return async (mcpContext, cancellationToken) =>
                {
                    if (!hasForcedRefresh)
                    {
                        hasForcedRefresh = true;

                        var httpContext = mcpContext.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                        await httpContext.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                        await httpContext.Response.CompleteAsync();
                        throw new Exception("This exception will not impact the client because the response has already been completed.");
                    }
                    else
                    {
                        return await next(mcpContext, cancellationToken);
                    }
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    lastAuthorizationUri = uri;
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(uri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mcp:tools files:read", requestedScope);
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
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(uri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
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

        Builder.Services.AddHttpContextAccessor();
        Builder.Services.AddMcpServer()
            .WithTools([
                McpServerTool.Create([McpServerTool(Name = "admin-tool")]
                async (IServiceProvider serviceProvider, ClaimsPrincipal user) =>
                {
                    if (!user.HasClaim("scope", adminScopes))
                    {
                        var httpContext = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                        httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                        httpContext.Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\", scope=\"{adminScopes}\"";
                        await httpContext.Response.CompleteAsync();

                        throw new Exception("This exception will not impact the client because the response has already been completed.");
                    }

                    return "Admin tool executed.";
                }),
            ]);

        string? requestedScope = null;

        await using var app = await StartMcpServerAsync();

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    var query = QueryHelpers.ParseQuery(uri.Query);
                    requestedScope = query["scope"].ToString();
                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("mcp:tools", requestedScope);

        var adminResult = await client.CallToolAsync("admin-tool", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Admin tool executed.", adminResult.Content[0].ToString());

        Assert.Equal(adminScopes, requestedScope);
    }

    [Fact]
    public async Task AuthorizationFails_WhenResourceMetadataPortDiffers()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.Resource = new Uri("http://localhost:5999");
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanAuthenticate_WithAuthorizationServerPathInsertionMetadata()
    {
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata!.AuthorizationServers = [new Uri($"{OAuthServerUrl}/tenant1")];
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
            options.ResourceMetadata!.AuthorizationServers = [new Uri($"{OAuthServerUrl}{issuerPath}")];
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
    public async Task CanAuthenticate_WithResourceMetadataPathFallbacks()
    {
        const string resourcePath = "/mcp";
        List<string> wellKnownRequests = [];

        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);
        await using var app = Builder.Build();

        var metadata = new ProtectedResourceMetadata
        {
            Resource = new Uri($"{McpServerUrl}{resourcePath}"),
            AuthorizationServers = { new Uri(OAuthServerUrl) },
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
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
        // CannotAuthenticate_WhenWwwAuthenticateResourceMetadataIsRootPath validates we won't fall back to root in this case.
        // CanAuthenticate_WithResourceMetadataPathFallbacks validates we will fall back to root when resource_metadata is missing.
        Builder.Services.Configure<AuthenticationOptions>(options => options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);
        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = new Uri($"{McpServerUrl}{configuredResourcePath}"),
                AuthorizationServers = { new Uri(OAuthServerUrl) },
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await McpClient.CreateAsync(
                transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        });

        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public async Task CannotAuthenticate_WhenWwwAuthenticateResourceMetadataIsRootPath()
    {
        const string requestedResourcePath = "/mcp/tools";

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = new Uri($"{McpServerUrl}"),
                AuthorizationServers = { new Uri(OAuthServerUrl) },
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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        var ex = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await McpClient.CreateAsync(
                transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
        });

        Assert.Contains("does not match", ex.Message);
    }

    [Fact]
    public void CloneResourceMetadataClonesAllProperties()
    {
        var propertyNames = typeof(ProtectedResourceMetadata).GetProperties().Select(property => property.Name).ToList();

        // Set metadata properties to non-default values to verify they're copied.
        var metadata = new ProtectedResourceMetadata
        {
            Resource = new Uri("https://example.com/resource"),
            AuthorizationServers = [new Uri("https://auth1.example.com"), new Uri("https://auth2.example.com")],
            BearerMethodsSupported = ["header", "body", "query"],
            ScopesSupported = ["read", "write", "admin"],
            JwksUri = new Uri("https://example.com/.well-known/jwks.json"),
            ResourceSigningAlgValuesSupported = ["RS256", "ES256"],
            ResourceName = "Test Resource",
            ResourceDocumentation = new Uri("https://docs.example.com"),
            ResourcePolicyUri = new Uri("https://example.com/policy"),
            ResourceTosUri = new Uri("https://example.com/terms"),
            TlsClientCertificateBoundAccessTokens = true,
            AuthorizationDetailsTypesSupported = ["payment_initiation", "account_information"],
            DpopSigningAlgValuesSupported = ["RS256", "PS256"],
            DpopBoundAccessTokensRequired = true
        };

        // Use reflection to call the internal CloneResourceMetadata method
        var handlerType = typeof(McpAuthenticationHandler);
        var cloneMethod = handlerType.GetMethod("CloneResourceMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(cloneMethod);

        var clonedMetadata = (ProtectedResourceMetadata?)cloneMethod.Invoke(null, [metadata, null]);
        Assert.NotNull(clonedMetadata);

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

        // Ensure we've checked every property. When new properties get added, we'll have to update this test along with the CloneResourceMetadata implementation.
        Assert.Empty(propertyNames);
    }
}
