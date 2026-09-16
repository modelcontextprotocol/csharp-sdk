using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

[CollectionDefinition(nameof(DisableConsoleParallelization), DisableParallelization = true)]
public sealed class DisableConsoleParallelization;

[Collection(nameof(DisableConsoleParallelization))]
public class DefaultAuthorizationUrlHandlerTests(ITestOutputHelper outputHelper) : OAuthTestBase(outputHelper)
{
    [Fact]
    public async Task RejectsAuthorizationCodeWithoutAbsoluteRedirectUrl()
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
            },
        }, HttpClient, LoggerFactory);

        var originalInput = Console.In;
        using var consoleInput = new StringReader("authorization-code");
        Console.SetIn(consoleInput);

        try
        {
            var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
                transport,
                loggerFactory: LoggerFactory,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("not a valid absolute URL", ex.Message);
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }
}
