using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public async Task Allows_Customizing_Route()
    {
        Builder.Services.AddMcpServer().WithHttpTransport();
        await using var app = Builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var httpClient = CreateHttpClient();
        using var response = await httpClient.GetAsync("http://localhost/mcp/sse", HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
