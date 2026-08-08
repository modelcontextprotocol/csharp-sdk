using ModelContextProtocol.AspNetCore.Tests.Utils;
using System.Text.Json;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

// The samples run TestOAuthServer standalone over plain HTTP so that clients which don't trust the
// ASP.NET Core developer certificate can still fetch its metadata. Whichever scheme it's hosted on,
// the discovery document has to describe that same origin, otherwise clients follow endpoints they
// can't reach and fall back to guessing.
public class TestOAuthServerHostingTests : KestrelInMemoryTest
{
    public TestOAuthServerHostingTests(ITestOutputHelper outputHelper)
        : base(outputHelper)
    {
        // The dev cert may not be installed on CI, so don't validate it when hosting over HTTPS.
        SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    }

    [Fact]
    public void StandaloneServer_UsesPlainHttp_UnlessHttpsIsRequested()
    {
        Assert.False(TestOAuthServer.Program.ShouldUseHttps([]));
        Assert.False(TestOAuthServer.Program.ShouldUseHttps(["--urls", "http://localhost:7029"]));
        Assert.True(TestOAuthServer.Program.ShouldUseHttps(["--https"]));
        Assert.True(TestOAuthServer.Program.ShouldUseHttps(["--HTTPS"]));

        // The switch carries no value, so it has to be gone before the host parses the rest.
        Assert.Equal(["--urls", "http://localhost:7029"],
            TestOAuthServer.Program.WithoutHttpsSwitch(["--https", "--urls", "http://localhost:7029"]));
    }

    [Theory]
    [InlineData(true, "https://localhost:7029")]
    [InlineData(false, "http://localhost:7029")]
    public async Task DiscoveryDocument_AdvertisesEndpointsOnTheHostedOrigin(bool useHttps, string expectedIssuer)
    {
        using var testCts = new CancellationTokenSource();
        var oauthServer = new TestOAuthServer.Program(XunitLoggerProvider, KestrelInMemoryTransport, useHttps);
        var runTask = oauthServer.RunServerAsync(cancellationToken: testCts.Token);

        try
        {
            await oauthServer.ServerStarted.WaitAsync(TestContext.Current.CancellationToken);

            using var response = await HttpClient.GetAsync(
                $"{expectedIssuer}/.well-known/oauth-authorization-server",
                TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            using var metadata = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            Assert.Equal(expectedIssuer, metadata.RootElement.GetProperty("issuer").GetString());

            foreach (var property in metadata.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is not JsonValueKind.String ||
                    (!property.Name.EndsWith("_endpoint", StringComparison.Ordinal) && property.Name != "jwks_uri"))
                {
                    continue;
                }

                Assert.StartsWith($"{expectedIssuer}/", property.Value.GetString());
            }
        }
        finally
        {
            testCts.Cancel();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
