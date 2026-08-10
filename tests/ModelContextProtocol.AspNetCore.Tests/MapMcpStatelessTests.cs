using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpStatelessTests(ITestOutputHelper outputHelper) : MapMcpStreamableHttpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => true;

    [Fact]
    public async Task ServerActivity_IncludesPerRequestProtocolVersion()
    {
        var activities = new List<Activity>();

        using (var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Experimental.ModelContextProtocol")
            .AddInMemoryExporter(activities)
            .Build())
        {
            Builder.Services.AddMcpServer()
                .WithHttpTransport(ConfigureStateless)
                .WithTools([McpServerTool.Create(() => "ok", new() { Name = "test-tool" })]);

            await using var app = Builder.Build();
            app.MapMcp();
            await app.StartAsync(TestContext.Current.CancellationToken);

            await using var client = await ConnectAsync(configureClient: options =>
                options.ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion);

            await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Contains(activities, activity =>
            activity.DisplayName == "tools/list" &&
            activity.Kind == ActivityKind.Server &&
            activity.GetTagItem("mcp.protocol.version") as string == McpProtocolVersions.July2026ProtocolVersion);
    }

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
