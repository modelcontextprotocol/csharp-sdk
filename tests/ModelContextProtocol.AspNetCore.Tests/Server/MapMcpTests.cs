using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.Server;

public class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public async Task Allows_Customizing_Route()
    {
        await using var app = Builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        using var response = await httpClient.GetAsync("http://localhost/mcp/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
