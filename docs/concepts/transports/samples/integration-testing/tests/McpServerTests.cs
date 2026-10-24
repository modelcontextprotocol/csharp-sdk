// <snippet_IntegrationTest>
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace IntegrationTestingMcpServer.Tests;

public class McpServerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task EchoTool_RoundTripsThroughStreamableHttp()
    {
        using HttpClient httpClient = factory.CreateClient();
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient);

        await using McpClient client = await McpClient.CreateAsync(
            transport,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            "echo",
            new Dictionary<string, object?> { ["message"] = "Hello MCP" },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.Single(result.Content.OfType<TextContentBlock>());
        Assert.Equal("Echo: Hello MCP", text.Text);
    }
}
// </snippet_IntegrationTest>
