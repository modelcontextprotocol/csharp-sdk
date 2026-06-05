using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpStatelessTests(ITestOutputHelper outputHelper) : MapMcpStreamableHttpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => true;

    [Fact]
    public async Task EnablePollingAsync_ThrowsInvalidOperationException_InStatelessMode()
    {
        InvalidOperationException? capturedException = null;
        var pollingTool = McpServerTool.Create(async (RequestContext<CallToolRequestParams> context) =>
        {
            try
            {
                await context.EnablePollingAsync(retryInterval: TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException ex)
            {
                capturedException = ex;
            }

            return "Complete";
        }, options: new() { Name = "polling_tool" });

        Builder.Services.AddMcpServer().WithHttpTransport(ConfigureStateless).WithTools([pollingTool]);

        await using var app = Builder.Build();
        app.MapMcp();

        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var mcpClient = await ConnectAsync();

        await mcpClient.CallToolAsync("polling_tool", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedException);
        Assert.Contains("stateless", capturedException.Message, StringComparison.OrdinalIgnoreCase);
    }
}
