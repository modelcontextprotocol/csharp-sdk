using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Protocol;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ModelContextProtocol.AspNetCore.Tests;

/// <summary>
/// Tests for the default Origin header validation applied by <see cref="McpEndpointRouteBuilderExtensions.MapMcp"/>
/// (see <see cref="HttpServerTransportOptions.AllowedOrigins"/> and
/// <see cref="HttpServerTransportOptions.DisableOriginValidation"/>).
/// </summary>
public class OriginValidationTests(ITestOutputHelper outputHelper) : KestrelInMemoryTest(outputHelper), IAsyncDisposable
{
    private WebApplication? _app;

    private async Task StartAsync(Action<HttpServerTransportOptions>? configureTransport = null)
    {
        Builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "OriginValidationTestServer",
                Version = "1.0.0",
            };
        }).WithHttpTransport(configureTransport);

        _app = Builder.Build();
        _app.MapMcp();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
        base.Dispose();
    }

    private static HttpRequestMessage CreateServerDiscoverRequest(string? origin = null)
    {
        const string discoverRequest = """
            {"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"OriginValidationTestClient","version":"1.0"},"io.modelcontextprotocol/clientCapabilities":{}}}}
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5000/")
        {
            Content = new StringContent(discoverRequest, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("MCP-Protocol-Version", McpProtocolVersions.July2026ProtocolVersion);
        request.Headers.Add("Mcp-Method", "server/discover");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }
        return request;
    }

    [Fact]
    public async Task Request_WithoutOriginHeader_IsAllowed()
    {
        await StartAsync();

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithSameOrigin_IsAllowed()
    {
        await StartAsync();

        // The origin's host and port match the request's Host header (localhost:5000).
        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "http://localhost:5000"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithSameHost_ButDifferentScheme_IsAllowed()
    {
        await StartAsync();

        // The scheme is intentionally not compared: TLS is often terminated at a reverse proxy, leaving
        // the request scheme as http while the browser origin uses https.
        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "https://localhost:5000"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithLoopbackOrigin_IsAllowed()
    {
        await StartAsync();

        // A browser running on the same machine (for example a frontend dev server on localhost:5173)
        // has a loopback origin that differs from the server's host and is allowed by default.
        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "http://localhost:5173"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("http://[::1]:5173")]
    public async Task Request_WithOtherLoopbackOrigin_IsAllowed(string origin)
    {
        await StartAsync();

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithCrossOrigin_IsRejected()
    {
        await StartAsync();

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "https://evil.example.com"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithMalformedOrigin_IsRejected()
    {
        await StartAsync();

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "not-a-valid-origin"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithConfiguredAllowedOrigin_IsAllowed()
    {
        await StartAsync(options => options.AllowedOrigins.Add("https://app.example.com"));

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "https://app.example.com"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithUnconfiguredOrigin_IsStillRejected_WhenOthersAreAllowed()
    {
        await StartAsync(options => options.AllowedOrigins.Add("https://app.example.com"));

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "https://other.example.com"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithCrossOrigin_WhenValidationDisabled_IsAllowed()
    {
        await StartAsync(options => options.DisableOriginValidation = true);

        using var response = await HttpClient.SendAsync(CreateServerDiscoverRequest(origin: "https://evil.example.com"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
