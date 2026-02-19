using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

public class McpAuthenticationHandlerTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper)
{
    [Fact]
    public async Task Challenge_WithRelativeResourceMetadataUri_SetsAbsoluteUrl()
    {
        const string metadataPath = "/.well-known/custom-relative";

        await using var app = await StartAuthenticationServerAsync(options =>
        {
            options.ResourceMetadataUri = new Uri(metadataPath, UriKind.Relative);
            options.ResourceMetadata!.Resource = "http://localhost:5000/challenge";
        });

        using var challengeResponse = await HttpClient.GetAsync(new Uri("/challenge", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, challengeResponse.StatusCode);
        var header = Assert.Single(challengeResponse.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", header.Scheme);
        Assert.Contains($"resource_metadata=\"http://localhost:5000{metadataPath}\"", header.Parameter);

        using var metadataResponse = await HttpClient.GetAsync(new Uri(metadataPath, UriKind.Relative), TestContext.Current.CancellationToken);
        metadataResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task MetadataRequest_CustomResourceMetadataUriWithoutResource_ThrowsInvalidOperationException()
    {
        const string metadataPath = "/.well-known/custom-metadata";

        await using var app = await StartAuthenticationServerAsync(options =>
        {
            options.ResourceMetadataUri = new Uri(metadataPath, UriKind.Relative);
        });

        using var response = await HttpClient.GetAsync(new Uri(metadataPath, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Error &&
            log.Exception is InvalidOperationException &&
            log.Exception.Message.Contains("ResourceMetadata.Resource could not be determined", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Challenge_WithAbsoluteResourceMetadataUri_SetsConfiguredUrl()
    {
        var metadataUri = new Uri("http://localhost:5000/.well-known/custom-absolute");

        await using var app = await StartAuthenticationServerAsync(options =>
        {
            options.ResourceMetadataUri = metadataUri;
            options.ResourceMetadata!.Resource = "http://localhost:5000/challenge";
        });

        using var challengeResponse = await HttpClient.GetAsync(new Uri("/challenge", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, challengeResponse.StatusCode);
        var header = Assert.Single(challengeResponse.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", header.Scheme);
        Assert.Contains($"resource_metadata=\"{metadataUri}\"", header.Parameter);

        using var metadataResponse = await HttpClient.GetAsync(metadataUri, TestContext.Current.CancellationToken);
        metadataResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task MetadataRequest_WithHostMismatch_LogsWarning()
    {
        var metadataUri = new Uri("http://expected-host:5000/.well-known/host-mismatch");

        await using var app = await StartAuthenticationServerAsync(options =>
        {
            options.ResourceMetadataUri = metadataUri;
        });

        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost:5000/.well-known/host-mismatch"));
        using var metadataResponse = await HttpClient.SendAsync(metadataRequest, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, metadataResponse.StatusCode);
        Assert.Contains(MockLoggerProvider.LogMessages, log =>
            log.LogLevel == LogLevel.Warning &&
            log.Message.Contains("Resource metadata request host", StringComparison.OrdinalIgnoreCase) &&
            log.Message.Contains("expected-host", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Challenge_WithDefaultMetadata_ComposesResourceSpecificEndpoint()
    {
        await using var app = await StartAuthenticationServerAsync();

        using var challengeResponse = await HttpClient.GetAsync(new Uri("/resource/tools/list", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, challengeResponse.StatusCode);
        var header = Assert.Single(challengeResponse.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", header.Scheme);
        Assert.Contains("resource_metadata=\"http://localhost:5000/.well-known/oauth-protected-resource/resource/tools/list\"", header.Parameter);
    }

    [Fact]
    public async Task Challenge_WithDefaultMetadata_AndPathBase_ComposesResourceSpecificEndpoint()
    {
        await using var app = await StartAuthenticationServerAsync(pathBase: new PathString("/api"));

        using var challengeResponse = await HttpClient.GetAsync(new Uri("/api/resource/tools/list", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, challengeResponse.StatusCode);
        var header = Assert.Single(challengeResponse.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", header.Scheme);
        Assert.Contains("resource_metadata=\"http://localhost:5000/api/.well-known/oauth-protected-resource/resource/tools/list\"", header.Parameter);
    }

    [Fact]
    public async Task MetadataRequest_DefaultEndpoint_SetsResourceFromSuffix()
    {
        await using var app = await StartAuthenticationServerAsync();

        using var metadataResponse = await HttpClient.GetAsync(new Uri("/.well-known/oauth-protected-resource/resource/tools", UriKind.Relative), TestContext.Current.CancellationToken);

        metadataResponse.EnsureSuccessStatusCode();

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken);
        Assert.NotNull(metadata);
        Assert.Equal("http://localhost:5000/resource/tools", metadata!.Resource);
    }

    [Fact]
    public async Task MetadataRequest_DefaultEndpoint_WithPathBase_SetsResourceFromSuffix()
    {
        await using var app = await StartAuthenticationServerAsync(pathBase: new PathString("/api"));

        using var metadataResponse = await HttpClient.GetAsync(new Uri("/api/.well-known/oauth-protected-resource/resource/tools", UriKind.Relative), TestContext.Current.CancellationToken);

        metadataResponse.EnsureSuccessStatusCode();

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<ProtectedResourceMetadata>(
            McpJsonUtilities.DefaultOptions,
            TestContext.Current.CancellationToken);
        Assert.NotNull(metadata);
        Assert.Equal("http://localhost:5000/api/resource/tools", metadata!.Resource);
    }

    private async Task<WebApplication> StartAuthenticationServerAsync(Action<McpAuthenticationOptions>? configureOptions = null, PathString? pathBase = null)
    {
        var authenticationBuilder = Builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = McpAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = McpAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        });

        authenticationBuilder.AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(McpAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.ResourceMetadata = new()
            {
                AuthorizationServers = ["https://localhost:7029"],
                ScopesSupported = ["mcp:tools"],
            };
            configureOptions?.Invoke(options);
        });

        authenticationBuilder.AddScheme<AuthenticationSchemeOptions, NoopBearerAuthenticationHandler>("Bearer", options => { });

        Builder.Services.AddAuthorization();

        var app = Builder.Build();
        if (pathBase is PathString basePath && basePath.HasValue)
        {
            app.UsePathBase(basePath);
        }
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/challenge", context => context.ChallengeAsync(McpAuthenticationDefaults.AuthenticationScheme));
        app.MapGet("/resource/{*resourcePath}", context => context.ChallengeAsync(McpAuthenticationDefaults.AuthenticationScheme));
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class NoopBearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
