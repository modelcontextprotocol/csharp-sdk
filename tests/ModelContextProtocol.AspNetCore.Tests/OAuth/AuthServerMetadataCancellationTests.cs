using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

// Authorization server metadata discovery treats a failed request to one well-known endpoint as a reason
// to try the next one. Cancellation is different: it must reach the caller as cancellation (or as the
// initialization timeout) instead of "Failed to find .well-known/... metadata".
public class AuthServerMetadataCancellationTests : OAuthTestBase
{
    public AuthServerMetadataCancellationTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
    }

    [Fact]
    public async Task CallerCancellation_DuringAuthServerMetadataDiscovery_ThrowsOperationCanceledException()
    {
        await using var app = await StartMcpServerAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var httpClient = CreateHttpClientWithHangingAuthServerMetadata(onMetadataRequest: cts.Cancel);
        await using var transport = CreateTransport(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => McpClient.CreateAsync(
            transport, loggerFactory: LoggerFactory, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task InitializationTimeout_DuringAuthServerMetadataDiscovery_ThrowsTimeoutException()
    {
        await using var app = await StartMcpServerAsync();

        using var httpClient = CreateHttpClientWithHangingAuthServerMetadata(onMetadataRequest: null);
        await using var transport = CreateTransport(httpClient);

        await Assert.ThrowsAsync<TimeoutException>(() => McpClient.CreateAsync(
            transport,
            new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(2) },
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    private HttpClient CreateHttpClientWithHangingAuthServerMetadata(Action? onMetadataRequest)
    {
        // The shared SocketsHttpHandler is owned and disposed by the base class.
        var httpClient = new HttpClient(new HangingAuthServerMetadataHandler(onMetadataRequest) { InnerHandler = SocketsHttpHandler }, disposeHandler: false);
        ConfigureHttpClient(httpClient);
        return httpClient;
    }

    private HttpClientTransport CreateTransport(HttpClient httpClient) =>
        new(new()
        {
            Endpoint = new(McpServerUrl),
            OAuth = new ClientOAuthOptions()
            {
                ClientId = "demo-client",
                ClientSecret = "demo-secret",
                RedirectUri = new Uri("http://localhost:1179/callback"),
                AuthorizationCallbackHandler = (_, _) => throw new InvalidOperationException("Authorization should not be reached."),
            },
        }, httpClient, LoggerFactory);

    // Simulates an authorization server whose metadata endpoints never respond.
    private sealed class HangingAuthServerMetadataHandler(Action? onMetadataRequest) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.StartsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal) ||
                path.StartsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                onMetadataRequest?.Invoke();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
