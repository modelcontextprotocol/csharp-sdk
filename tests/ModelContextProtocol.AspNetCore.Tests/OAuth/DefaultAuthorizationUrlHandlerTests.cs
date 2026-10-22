using ModelContextProtocol.Client;

namespace ModelContextProtocol.AspNetCore.Tests.OAuth;

[CollectionDefinition(nameof(DisableConsoleParallelization), DisableParallelization = true)]
public sealed class DisableConsoleParallelization;

[Collection(nameof(DisableConsoleParallelization))]
public class DefaultAuthorizationUrlHandlerTests(ITestOutputHelper outputHelper) : OAuthTestBase(outputHelper)
{
    [Theory]
    [InlineData("authorization-code", "not a valid absolute URL")]
    [InlineData("http://localhost:1179/callback?code=unused-code", "did not include the required state parameter")]
    [InlineData("http://localhost:1179/callback?code=unused-code&state=unexpected-state", "state did not match")]
    public async Task RejectsInvalidAuthorizationResponse(string input, string expectedMessage)
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
        using var consoleInput = new StringReader(input);
        Console.SetIn(consoleInput);

        try
        {
            var ex = await Assert.ThrowsAsync<McpException>(() => McpClient.CreateAsync(
                transport, loggerFactory: LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains(expectedMessage, ex.Message);
            Assert.Equal(0, TestOAuthServer.AuthorizationCodeTokenRequestCount);
        }
        finally
        {
            Console.SetIn(originalInput);
        }
    }
}
