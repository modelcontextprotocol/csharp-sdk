using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpTaskIntegrationTests(ITestOutputHelper testOutputHelper) : KestrelInMemoryTest(testOutputHelper)
{
    [Fact]
    public async Task WithTasks_CanCallToolOverHttp()
    {
        Builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTasks(new InMemoryMcpTaskStore { DefaultPollIntervalMs = 10 })
            .WithTools<TestTools>();

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new("http://localhost:5000") },
            HttpClient,
            LoggerFactory);
        await using var client = await McpClient.CreateAsync(
            transport,
            loggerFactory: LoggerFactory,
            cancellationToken: TestContext.Current.CancellationToken);

        var result = await client.CallToolWithPollingAsync(
            new CallToolRequestParams { Name = "test" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello World!", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [McpServerToolType]
    private sealed class TestTools
    {
        [McpServerTool(Name = "test")]
        public static string Test() => "Hello World!";
    }
}
