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

        var clientToolCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "DoubleValue") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call DoubleValue" &&
            a.Kind == ActivityKind.Client &&
            a.Status == ActivityStatusCode.Unset);

        var serverToolCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "DoubleValue") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call DoubleValue" &&
            a.Kind == ActivityKind.Server &&
            a.Status == ActivityStatusCode.Unset);

        Assert.Equal(clientToolCall.SpanId, serverToolCall.ParentSpanId);
        Assert.Equal(clientToolCall.TraceId, serverToolCall.TraceId);

        var clientListToolsCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/list") &&
            a.DisplayName == "tools/list" &&
            a.Kind == ActivityKind.Client &&
            a.Status == ActivityStatusCode.Unset);

        var serverListToolsCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/list") &&
            a.DisplayName == "tools/list" &&
            a.Kind == ActivityKind.Server &&
            a.Status == ActivityStatusCode.Unset);

        Assert.Equal(clientListToolsCall.SpanId, serverListToolsCall.ParentSpanId);
        Assert.Equal(clientListToolsCall.TraceId, serverListToolsCall.TraceId);
    }

    [Fact]
    public async Task Session_FailedToolCall()
    {
        var activities = new List<Activity>();

        using (var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource("Experimental.ModelContextProtocol")
            .AddInMemoryExporter(activities)
            .Build())
        {
            await RunConnected(async (client, server) =>
            {
                await client.CallToolAsync("Throw", cancellationToken: TestContext.Current.CancellationToken);
                await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync("does-not-exist", cancellationToken: TestContext.Current.CancellationToken));
            });
        }

        Assert.NotEmpty(activities);

        var throwToolClient = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "Throw") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call Throw" &&
            a.Kind == ActivityKind.Client);

        Assert.Equal(ActivityStatusCode.Error, throwToolClient.Status);

        var throwToolServer = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "Throw") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call Throw" &&
            a.Kind == ActivityKind.Server);

        Assert.Equal(ActivityStatusCode.Error, throwToolServer.Status);

        var doesNotExistToolClient = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "does-not-exist") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call does-not-exist" &&
            a.Kind == ActivityKind.Client);

        Assert.Equal(ActivityStatusCode.Error, doesNotExistToolClient.Status);
        Assert.Equal("-32603", doesNotExistToolClient.Tags.Single(t => t.Key == "rpc.jsonrpc.error_code").Value);

        var doesNotExistToolServer = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "mcp.tool.name" && t.Value == "does-not-exist") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call does-not-exist" &&
            a.Kind == ActivityKind.Server);

        Assert.Equal(ActivityStatusCode.Error, doesNotExistToolServer.Status);
        Assert.Equal("-32603", doesNotExistToolClient.Tags.Single(t => t.Key == "rpc.jsonrpc.error_code").Value);
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
                        ToolCollection = [
                            McpServerTool.Create((int amount) => amount * 2, new() { Name = "DoubleValue", Description = "Doubles the value." }),
                            McpServerTool.Create(() => { throw new Exception("boom"); }, new() { Name = "Throw", Description = "Throws error." }),
                        ],
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
