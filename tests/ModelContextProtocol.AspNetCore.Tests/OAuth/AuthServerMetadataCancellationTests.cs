using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

// Authorization server metadata discovery treats a failed request to one well-known endpoint as a reason
// to try the next one. Cancellation is different: it must reach the caller as cancellation (or as the
// initialization timeout) instead of "Failed to find .well-known/... metadata".
public class AuthServerMetadataCancellationTests(ITestOutputHelper outputHelper) : OAuthTestBase(outputHelper)
{
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(5);
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly AsyncGate _metadata = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthServerMetadataDiscovery_ObservesCallerAndInitializationCancellation(bool initializationTimeout)
    {
        await using var app = await StartMcpServerAsync();
        using var httpClient = CreateHttpClientWithGatedAuthServerMetadata();
        await using var transport = CreateTransport(httpClient);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var options = new McpClientOptions
        {
            TimeProvider = _timeProvider,
            DiscoverProbeTimeout = ProbeBudget,
            InitializationTimeout = ProbeBudget * 4,
        };

        var connecting = McpClient.CreateAsync(transport, options, LoggerFactory, caller.Token);
        await _metadata.WaitUntilEnteredAsync(connecting);
        var metadataRequestToken = _metadata.Token;

        // The probe timeout is armed, but it must not fire while OAuth loads the metadata.
        _timeProvider.Advance(ProbeBudget * 2);
        Assert.False(metadataRequestToken.IsCancellationRequested);

        if (initializationTimeout)
        {
            _timeProvider.Advance(options.InitializationTimeout);
            var error = await Assert.ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
            Assert.Equal("Initialization timed out", error.Message);
        }
        else
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
        }

        await _metadata.Canceled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    private HttpClient CreateHttpClientWithGatedAuthServerMetadata()
    {
        // The shared SocketsHttpHandler is owned and disposed by the base class.
        var httpClient = new HttpClient(new GatedAuthServerMetadataHandler(_metadata) { InnerHandler = SocketsHttpHandler }, disposeHandler: false);
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

    // Holds the authorization server metadata requests until they are canceled.
    private sealed class GatedAuthServerMetadataHandler(AsyncGate gate) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.StartsWith("/.well-known/oauth-authorization-server", StringComparison.Ordinal) ||
                path.StartsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                await gate.WaitAsync(cancellationToken);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
