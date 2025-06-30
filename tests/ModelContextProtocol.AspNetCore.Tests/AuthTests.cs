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
using Xunit.Sdk;

namespace ModelContextProtocol.AspNetCore.Tests;

public class AuthTests : KestrelInMemoryTest, IAsyncDisposable
{
    private const string McpServerUrl = "http://localhost:5000";
    private const string OAuthServerUrl = "https://localhost:7029";
    private const string ClientId = "demo-client";

    private readonly CancellationTokenSource _testCts = new();
    private readonly Task _oAuthRunTask;

    public AuthTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        // Let the HandleAuthorizationUrlAsync take a look at the Location header
        SocketsHttpHandler.AllowAutoRedirect = false;

        var oAuthServerProgram = new TestOAuthServer.Program(XunitLoggerProvider, KestrelInMemoryTransport);
        _oAuthRunTask = oAuthServerProgram.RunServerAsync(cancellationToken: _testCts.Token);

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
                ValidAudience = ClientId,
                ValidIssuer = OAuthServerUrl,
                NameClaimType = "name",
                RoleClaimType = "roles"
            };
        })
        .AddMcp(options =>
        {
            options.ProtectedResourceMetadataProvider = context =>
            {
                var metadata = new ProtectedResourceMetadata
                {
                    Resource = new Uri(McpServerUrl),
                    BearerMethodsSupported = { "header" },
                    AuthorizationServers = { new Uri(OAuthServerUrl) }
                };

                metadata.ScopesSupported.AddRange([
                    "mcp:tools"
                ]);

                return metadata;
            };
        });

        Builder.Services.AddAuthorization();
    }

    public async ValueTask DisposeAsync()
    {
        _testCts.Cancel();
        try
        {
            await _oAuthRunTask;
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

        await using var transport = new SseClientTransport(new()
        {
            Endpoint = new("http://localhost:5000"),
            OAuth = new()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClientFactory.CreateAsync(
            transport,  loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CannotAuthenticate_WithoutOAuthConfiguration()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();

        await using var app = Builder.Build();

        app.MapMcp().RequireAuthorization();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new SseClientTransport(new()
        {
            Endpoint = new("http://localhost:5000"),
        }, HttpClient, LoggerFactory);

        var httpEx = await Assert.ThrowsAsync<HttpRequestException>(async () => await McpClientFactory.CreateAsync(
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

        await using var transport = new SseClientTransport(new()
        {
            Endpoint = new("http://localhost:5000"),
            OAuth = new()
            {
                ClientId = "unregistered-demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // The EqualException is thrown by HandleAuthorizationUrlAsync when the /authorize request gets a 400
        var equalEx = await Assert.ThrowsAsync<EqualException>(async () => await McpClientFactory.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    private async Task<string?> HandleAuthorizationUrlAsync(Uri authorizationUrl, Uri redirectUri, CancellationToken cancellationToken)
    {
        var redirectResponse = await HttpClient.GetAsync(authorizationUrl, cancellationToken);
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
