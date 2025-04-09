using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.IO.Pipelines;

namespace ModelContextProtocol.Tests;

[Collection(nameof(DisableParallelization))]
public class DiagnosticTests
{
    [Fact]
    public async Task Session_TracksActivities()
    {
        var activities = new List<Activity>();

        using (var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Experimental.ModelContextProtocol")
            .AddInMemoryExporter(activities)
            .Build())
        {
            await RunConnected(async (client, server) =>
            {
                var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
                Assert.NotNull(tools);
                Assert.NotEmpty(tools);

                var tool = tools.First(t => t.Name == "DoubleValue");
                await tool.InvokeAsync(new() { ["amount"] = 42 }, TestContext.Current.CancellationToken);
            });
        }

        Assert.NotEmpty(activities);

        Activity toolCallActivity = activities.First(a =>
            a.Tags.Any(t => t.Key == "rpc.method" && t.Value == "tools/call"));
        Assert.Equal("DoubleValue", toolCallActivity.Tags.First(t => t.Key == "mcp.request.params.name").Value);
    }

    private static async Task RunConnected(Func<IMcpClient, IMcpServer, Task> action)
    {
        Pipe clientToServerPipe = new(), serverToClientPipe = new();
        StreamServerTransport serverTransport = new(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream());
        StreamClientTransport clientTransport = new(clientToServerPipe.Writer.AsStream(), serverToClientPipe.Reader.AsStream());

        Task serverTask;

        await using (IMcpServer server = McpServerFactory.Create(serverTransport, new()
            {
                Capabilities = new()
                {
                    Tools = new()
                    {
                        ToolCollection = [McpServerTool.Create((int amount) => amount * 2, new() { Name = "DoubleValue", Description = "Doubles the value." })],
                    }
                }
            }))
        {
            serverTask = server.RunAsync(TestContext.Current.CancellationToken);

            await using (IMcpClient client = await McpClientFactory.CreateAsync(
                clientTransport,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                await action(client, server);
            }
        }

        await serverTask;
    }
}
