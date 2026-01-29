using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

/// <summary>
/// Tests for client_credentials OAuth flow with various authentication methods.
/// </summary>
public class ClientCredentialsTests : OAuthTestBase
{
    public ClientCredentialsTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task CanAuthenticate_WithClientCredentials_ClientSecretPost()
    {
        await using var app = await StartMcpServerAsync();

        // Use client_credentials flow with client_secret_post authentication
        // Note: No AuthorizationRedirectDelegate means machine-to-machine flow will be attempted
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions
            {
                ClientId = "client-credentials-post",
                ClientSecret = "cc-secret-post",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                // No AuthorizationRedirectDelegate - triggers client_credentials flow
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task CanAuthenticate_WithClientCredentials_ClientSecretBasic()
    {
        await using var app = await StartMcpServerAsync();

        // Use client_credentials flow with client_secret_basic authentication
        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions
            {
                ClientId = "client-credentials-basic",
                ClientSecret = "cc-secret-basic",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                // No AuthorizationRedirectDelegate - triggers client_credentials flow
            },
        }, HttpClient, LoggerFactory);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(client);
    }

    [Fact]
    public async Task DoesNotLoopIndefinitely_WhenTokensAlwaysRejected()
    {
        // Set up a server that always returns 401 even after authentication
        // This simulates a buggy MCP server that never accepts tokens
        var app = Builder.Build();
        
        // Add middleware that always returns 401 with the MCP auth challenge
        app.Use((HttpContext context, RequestDelegate next) =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = $"Bearer realm=\"{OAuthServerUrl}\" resource_metadata=\"{McpServerUrl}/.well-known/oauth-protected-resource\"";
            return context.Response.WriteAsync("Unauthorized");
        });

        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var _ = app;

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions
            {
                ClientId = "client-credentials-post",
                ClientSecret = "cc-secret-post",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                // No AuthorizationRedirectDelegate - triggers client_credentials flow
            },
        }, HttpClient, LoggerFactory);

        // Should throw McpException after max retries, not loop indefinitely
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await McpClient.CreateAsync(transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Maximum repeated authentication failure limit", ex.Message);
    }
}
