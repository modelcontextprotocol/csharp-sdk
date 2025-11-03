using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Xunit.Sdk;

namespace ModelContextProtocol.AspNetCore.Tests;

public class AuthTests : KestrelInMemoryTest, IAsyncDisposable
{
    private const string McpServerUrl = "http://localhost:5000";
    private const string OAuthServerUrl = "https://localhost:7029";

    private readonly CancellationTokenSource _testCts = new();
    private readonly TestOAuthServer.Program _testOAuthServer;
    private readonly Task _testOAuthRunTask;

    private Uri? _lastAuthorizationUri;

    public AuthTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        // Let the HandleAuthorizationUrlAsync take a look at the Location header
        SocketsHttpHandler.AllowAutoRedirect = false;
        // The dev cert may not be installed on the CI, but AddJwtBearer requires an HTTPS backchannel by default.
        // The easiest workaround is to disable cert validation for testing purposes.
        SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _testOAuthServer = new TestOAuthServer.Program(XunitLoggerProvider, KestrelInMemoryTransport);
        _testOAuthRunTask = _testOAuthServer.RunServerAsync(cancellationToken: _testCts.Token);

        Builder.Services.AddAuthentication(options =>
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
                RoleClaimType = "roles"
            };
        })
        .AddMcp(options =>
        {
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = new Uri(McpServerUrl),
                AuthorizationServers = { new Uri(OAuthServerUrl) },
                ScopesSupported = ["mcp:tools"]
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
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _testCts.Dispose();
        }
    }

    [Fact]
    public async Task CanAuthenticate()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CannotAuthenticate_WithoutOAuthConfiguration()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

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
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
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
                },
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithTokenRefresh()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                ClientId = "test-refresh-client",
                ClientSecret = "test-refresh-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // The test-refresh-client should get an expired token first,
        // then automatically refresh it to get a working token
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(_testOAuthServer.HasIssuedRefreshToken);
    }

    [Fact]
    public async Task CanAuthenticate_WithExtraParams()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

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
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
                AdditionalAuthorizationParameters = new Dictionary<string, string>
                {
                    ["custom_param"] = "custom_value",
                }
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(_lastAuthorizationUri?.Query);
        Assert.Contains("custom_param=custom_value", _lastAuthorizationUri?.Query);
    }

    [Fact]
    public async Task CannotOverrideExistingParameters_WithExtraParams()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

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

        var clonedMetadata = (ProtectedResourceMetadata?)cloneMethod.Invoke(null, [metadata]);
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

    [Fact]
    public async Task ResourceMetadataEndpoint_ResolvesCorrectly_WithAbsoluteUriIncludingPathComponent()
    {
        const string enterprisePath = "/enterprise";
        const string metadataPath = "/.well-known/oauth-protected-resource";
        const string fullMetadataPath = enterprisePath + metadataPath;
        
        // Configure the builder with a fresh authentication setup for this test
        var testBuilder = WebApplication.CreateSlimBuilder();
        testBuilder.Services.AddSingleton(XunitLoggerProvider);
        
        testBuilder.Services.AddAuthentication(options =>
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
                RoleClaimType = "roles"
            };
        })
        .AddMcp(options =>
        {
            // Set an absolute URI with a path component after the host
            options.ResourceMetadataUri = new Uri($"{McpServerUrl}{fullMetadataPath}");
            options.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = new Uri(McpServerUrl),
                AuthorizationServers = { new Uri(OAuthServerUrl) },
                ScopesSupported = ["mcp:tools"]
            };
        });

        testBuilder.Services.AddAuthorization();
        testBuilder.Services.AddMcpServer().WithHttpTransport();

        await using var app = testBuilder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        // Test that the metadata endpoint responds at the correct path with the path component
        var metadataResponse = await HttpClient.GetAsync($"{McpServerUrl}{fullMetadataPath}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);
        
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions, TestContext.Current.CancellationToken);
        
        Assert.NotNull(metadata);
        Assert.Equal(new Uri(McpServerUrl), metadata.Resource);
        Assert.Contains(new Uri(OAuthServerUrl), metadata.AuthorizationServers);
        Assert.Contains("mcp:tools", metadata.ScopesSupported);

        // Test that a request without the path component returns 401 (not the metadata)
        var unauthorizedResponse = await HttpClient.GetAsync($"{McpServerUrl}/message", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        
        // Verify the WWW-Authenticate header includes the full absolute URI with path component
        var wwwAuthHeader = unauthorizedResponse.Headers.WwwAuthenticate.FirstOrDefault();
        Assert.NotNull(wwwAuthHeader);
        Assert.Equal("Bearer", wwwAuthHeader.Scheme);
        Assert.Contains($"resource_metadata=\"{McpServerUrl}{fullMetadataPath}\"", wwwAuthHeader.Parameter);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        _lastAuthorizationUri = authorizationUri;

        var redirectResponse = await HttpClient.GetAsync(authorizationUri, cancellationToken);
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
