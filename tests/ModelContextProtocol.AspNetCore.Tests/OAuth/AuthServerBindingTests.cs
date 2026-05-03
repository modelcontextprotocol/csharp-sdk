using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

/// <summary>
/// Tests that verify the client enforces authorization server binding per MCP SEP-2352.
/// Clients MUST maintain separate credentials per authorization server and MUST NOT
/// reuse credentials from a different authorization server when the AS changes.
/// </summary>
public class AuthServerBindingTests : OAuthTestBase
{
    private const string ClientMetadataDocumentUrl = $"{OAuthServerUrl}/client-metadata/cimd-client.json";
    private const string AlternateAuthServerIssuer = $"{OAuthServerUrl}/v2";

    public AuthServerBindingTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task AuthServerChange_WithPreRegisteredCredentials_ThrowsMcpException()
    {
        // Arrange: configure the MCP server to dynamically switch its authorization server
        var hasChangedAs = false;

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnResourceMetadataRequest = ctx =>
            {
                ctx.ResourceMetadata = new ProtectedResourceMetadata
                {
                    AuthorizationServers = { hasChangedAs ? AlternateAuthServerIssuer : OAuthServerUrl },
                    ScopesSupported = ["mcp:tools"],
                };
                return Task.CompletedTask;
            };
        });

        Builder.Services.AddMcpServer(options => options.ToolCollection = new());

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            // After the AS changes, return 401 for authenticated MCP requests to trigger re-authentication.
            app.Use(async (context, next) =>
            {
                if (hasChangedAs && context.Request.Method == HttpMethods.Post && context.Request.Path == "/" &&
                    context.Request.Headers.Authorization.Count > 0)
                {
                    await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                    await context.Response.StartAsync(context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    return;
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                // Pre-registered credentials: ClientId is provided
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationRedirectDelegate = HandleAuthorizationUrlAsync,
            },
        }, HttpClient, LoggerFactory);

        // Act: connect initially with AS1 - should succeed
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        // Simulate the authorization server changing
        hasChangedAs = true;

        // Assert: a subsequent request must throw because pre-registered credentials are AS-specific
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("authorization server has changed", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(OAuthServerUrl, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AlternateAuthServerIssuer, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthServerChange_WithDcrCredentials_TriggersReregistration()
    {
        // Arrange: configure the MCP server to dynamically switch its authorization server
        var hasChangedAs = false;
        var dcrCallCount = 0;
        var authDelegateCallCount = 0;

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnResourceMetadataRequest = ctx =>
            {
                ctx.ResourceMetadata = new ProtectedResourceMetadata
                {
                    AuthorizationServers = { hasChangedAs ? AlternateAuthServerIssuer : OAuthServerUrl },
                    ScopesSupported = ["mcp:tools"],
                };
                return Task.CompletedTask;
            };
        });

        Builder.Services.AddMcpServer(options => options.ToolCollection = new());

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (hasChangedAs && context.Request.Method == HttpMethods.Post && context.Request.Path == "/" &&
                    context.Request.Headers.Authorization.Count > 0)
                {
                    await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                    await context.Response.StartAsync(context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    return;
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                // No ClientId: DCR will be used
                RedirectUri = new Uri("http://localhost:1179/callback"),
                DynamicClientRegistration = new()
                {
                    ClientName = "Test MCP Client",
                    ResponseDelegate = (_, _) =>
                    {
                        dcrCallCount++;
                        return Task.CompletedTask;
                    },
                },
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCallCount++;
                    // On the second call (after AS change), return null to stop the flow.
                    // This lets us verify DCR happened without needing the full token exchange to succeed.
                    if (authDelegateCallCount > 1)
                    {
                        return Task.FromResult<string?>(null);
                    }

                    return HandleAuthorizationUrlAsync(uri, redirect, ct);
                },
            },
        }, HttpClient, LoggerFactory);

        // Act: connect initially with AS1 using DCR - should succeed
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, dcrCallCount); // DCR happened once for AS1

        // Simulate the authorization server changing
        hasChangedAs = true;

        // This will fail because the auth delegate returns null (by design to observe the re-registration),
        // but DCR with the new AS must have occurred first.
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Assert: DCR was triggered a second time for the new AS
        Assert.Equal(2, dcrCallCount);
        Assert.Contains("authorization code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthServerChange_WithCimdCredentials_ClearsTokensButKeepsPortableClientId()
    {
        // Arrange: configure the MCP server to dynamically switch its authorization server
        var hasChangedAs = false;
        var authDelegateCallCount = 0;
        Uri? secondAuthUri = null;

        Builder.Services.Configure<McpAuthenticationOptions>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Events.OnResourceMetadataRequest = ctx =>
            {
                ctx.ResourceMetadata = new ProtectedResourceMetadata
                {
                    AuthorizationServers = { hasChangedAs ? AlternateAuthServerIssuer : OAuthServerUrl },
                    ScopesSupported = ["mcp:tools"],
                };
                return Task.CompletedTask;
            };
        });

        Builder.Services.AddMcpServer(options => options.ToolCollection = new());

        await using var app = await StartMcpServerAsync(configureMiddleware: app =>
        {
            app.Use(async (context, next) =>
            {
                if (hasChangedAs && context.Request.Method == HttpMethods.Post && context.Request.Path == "/" &&
                    context.Request.Headers.Authorization.Count > 0)
                {
                    await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
                    await context.Response.StartAsync(context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    return;
                }

                await next(context);
            });
        });

        await using var transport = new HttpClientTransport(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new()
            {
                // CIMD: client ID is a portable HTTPS URL resolved by the AS
                RedirectUri = new Uri("http://localhost:1179/callback"),
                ClientMetadataDocumentUri = new Uri(ClientMetadataDocumentUrl),
                AuthorizationRedirectDelegate = (uri, redirect, ct) =>
                {
                    authDelegateCallCount++;
                    if (authDelegateCallCount == 1)
                    {
                        return HandleAuthorizationUrlAsync(uri, redirect, ct);
                    }

                    // Capture the second auth URI to verify the CIMD client ID is preserved
                    secondAuthUri = uri;
                    // Return null to stop the flow - we've captured what we need
                    return Task.FromResult<string?>(null);
                },
            },
        }, HttpClient, LoggerFactory);

        // Act: connect initially with AS1 using CIMD - should succeed
        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, authDelegateCallCount); // One auth code flow for AS1

        // Simulate the authorization server changing
        hasChangedAs = true;

        // This will fail because we return null from the auth delegate,
        // but the important thing is to verify the CIMD client ID is still used.
        var ex = await Assert.ThrowsAsync<McpException>(async () =>
            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken));

        // Assert: the auth code flow was triggered again (tokens were cleared)
        Assert.Equal(2, authDelegateCallCount);

        // Assert: the CIMD client ID (portable URL) was used in the second auth request,
        // not a new DCR-registered ID
        Assert.NotNull(secondAuthUri);
        Assert.Contains(Uri.EscapeDataString(ClientMetadataDocumentUrl), secondAuthUri.Query, StringComparison.OrdinalIgnoreCase);
    }
}
