using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace ModelContextProtocol.Tests;

[Collection(nameof(DisableParallelization))]
public class DiagnosticTests
{
    [Fact]
    public async Task Session_TracksActivities()
    {
        var activities = new List<Activity>();
        var clientToServerLog = new List<string>();

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
            }, clientToServerLog);

            // Wait for server-side activities to be exported. The server processes messages
            // via fire-and-forget tasks, so activities may not be immediately available
            // after the client operation completes. Wait for the specific activity we need
            // rather than a count, as other server activities may be exported first.
            await WaitForAsync(() => activities.Any(a =>
                a.DisplayName == "tools/call DoubleValue" && a.Kind == ActivityKind.Server));
        }

        Assert.NotEmpty(activities);

        var clientToolCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "DoubleValue") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.Tags.Any(t => t.Key == "gen_ai.operation.name" && t.Value == "execute_tool") &&
            a.DisplayName == "tools/call DoubleValue" &&
            a.Kind == ActivityKind.Client &&
            a.Status == ActivityStatusCode.Unset);

        // Per semantic conventions: mcp.protocol.version should be present after initialization
        Assert.Contains(clientToolCall.Tags, t => t.Key == "mcp.protocol.version" && !string.IsNullOrEmpty(t.Value));

        var serverToolCall = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "DoubleValue") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.Tags.Any(t => t.Key == "gen_ai.operation.name" && t.Value == "execute_tool") &&
            a.DisplayName == "tools/call DoubleValue" &&
            a.Kind == ActivityKind.Server &&
            a.Status == ActivityStatusCode.Unset);

        // Per semantic conventions: mcp.protocol.version should be present after initialization
        Assert.Contains(serverToolCall.Tags, t => t.Key == "mcp.protocol.version" && !string.IsNullOrEmpty(t.Value));

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

        // Validate that the client trace context encoded to request.params._meta[traceparent]
        using var listToolsJson = JsonDocument.Parse(clientToServerLog.First(s => s.Contains("\"method\":\"tools/list\"")));
        var metaJson = listToolsJson.RootElement.GetProperty("params").GetProperty("_meta").GetRawText();
        Assert.Equal($$"""{"traceparent":"00-{{clientListToolsCall.TraceId}}-{{clientListToolsCall.SpanId}}-01"}""", metaJson);
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
                await Assert.ThrowsAsync<McpProtocolException>(async () => await client.CallToolAsync("does-not-exist", cancellationToken: TestContext.Current.CancellationToken));
            }, []);

            // Wait for server-side activities to be exported. Wait for specific activities
            // rather than a count, as other server activities may be exported first.
            await WaitForAsync(() =>
                activities.Any(a => a.DisplayName == "tools/call Throw" && a.Kind == ActivityKind.Server) &&
                activities.Any(a => a.DisplayName == "tools/call does-not-exist" && a.Kind == ActivityKind.Server));
        }

        Assert.NotEmpty(activities);

        var throwToolClient = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "Throw") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call Throw" &&
            a.Kind == ActivityKind.Client);

        Assert.Equal(ActivityStatusCode.Error, throwToolClient.Status);

        var throwToolServer = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "Throw") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call Throw" &&
            a.Kind == ActivityKind.Server);

        Assert.Equal(ActivityStatusCode.Error, throwToolServer.Status);

        var doesNotExistToolClient = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "does-not-exist") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call does-not-exist" &&
            a.Kind == ActivityKind.Client);

        Assert.Equal(ActivityStatusCode.Error, doesNotExistToolClient.Status);
        Assert.Equal("-32602", doesNotExistToolClient.Tags.Single(t => t.Key == "rpc.response.status_code").Value);

        var doesNotExistToolServer = Assert.Single(activities, a =>
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "does-not-exist") &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.DisplayName == "tools/call does-not-exist" &&
            a.Kind == ActivityKind.Server);

        Assert.Equal(ActivityStatusCode.Error, doesNotExistToolServer.Status);
        Assert.Equal("-32602", doesNotExistToolClient.Tags.Single(t => t.Key == "rpc.response.status_code").Value);
    }

    [Fact]
    public async Task Session_McpAttributesAddedToOuterExecuteToolActivity()
    {
        // This test simulates the scenario where FunctionInvokingChatClient creates an outer
        // "execute_tool" activity, and MCP should add its attributes to that activity instead
        // of creating a new one.
        string outerSourceName = "TestOuterSource";
        var activities = new List<Activity>();

        using var outerSource = new ActivitySource(outerSourceName);

        using (var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
            .AddSource(outerSourceName)
            .AddSource("Experimental.ModelContextProtocol")
            .AddInMemoryExporter(activities)
            .Build())
        {
            await RunConnected(async (client, server) =>
            {
                // Simulate FunctionInvokingChatClient creating an outer activity
                using var outerActivity = outerSource.StartActivity("execute_tool DoubleValue");
                Assert.NotNull(outerActivity);

                // Now call the MCP tool - MCP should augment the outer activity
                var tool = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
                    .First(t => t.Name == "DoubleValue");
                await tool.InvokeAsync(new() { ["amount"] = 42 }, TestContext.Current.CancellationToken);
            }, []);

            // Wait for server-side activities to be exported. Wait for specific activities
            // rather than a count, as other server activities may be exported first.
            await WaitForAsync(() => activities.Any(a =>
                a.DisplayName == "tools/call DoubleValue" && a.Kind == ActivityKind.Server));
        }

        // The outer activity should have MCP-specific attributes added to it
        var outerExecuteToolActivity = Assert.Single(activities, a =>
            a.Source.Name == outerSourceName &&
            a.DisplayName == "execute_tool DoubleValue" &&
            a.Kind == ActivityKind.Internal);

        // MCP should have added its attributes to the outer activity
        Assert.Contains(outerExecuteToolActivity.Tags, t => t.Key == "mcp.method.name" && t.Value == "tools/call");
        Assert.Contains(outerExecuteToolActivity.Tags, t => t.Key == "gen_ai.tool.name" && t.Value == "DoubleValue");
        Assert.Contains(outerExecuteToolActivity.Tags, t => t.Key == "gen_ai.operation.name" && t.Value == "execute_tool");

        // Verify that no separate MCP client activity was created for this tool call
        var mcpClientActivities = activities.Where(a =>
            a.Source.Name == "Experimental.ModelContextProtocol" &&
            a.Kind == ActivityKind.Client &&
            a.Tags.Any(t => t.Key == "mcp.method.name" && t.Value == "tools/call") &&
            a.Tags.Any(t => t.Key == "gen_ai.tool.name" && t.Value == "DoubleValue"));
        Assert.Empty(mcpClientActivities);
    }

    private static async Task RunConnected(Func<McpClient, McpServer, Task> action, List<string> clientToServerLog)
    {
        Pipe clientToServerPipe = new(), serverToClientPipe = new();
        StreamServerTransport serverTransport = new(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream());
        StreamClientTransport clientTransport = new(new LoggingStream(
            clientToServerPipe.Writer.AsStream(), clientToServerLog.Add), serverToClientPipe.Reader.AsStream());

        Task serverTask;

        await using (McpServer server = McpServer.Create(serverTransport, new()
            {
                ToolCollection = [
                    McpServerTool.Create((int amount) => amount * 2, new() { Name = "DoubleValue", Description = "Doubles the value." }),
                    McpServerTool.Create(() => { throw new Exception("boom"); }, new() { Name = "Throw", Description = "Throws error." }),
                ]
            }))
        {
            serverTask = server.RunAsync(TestContext.Current.CancellationToken);

            await using (McpClient client = await McpClient.CreateAsync(
                clientTransport,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                await action(client, server);
            }
        }

        await serverTask;
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}

public class LoggingStream : Stream
{
    private readonly Stream _innerStream;
    private readonly Action<string> _logAction;

    public LoggingStream(Stream innerStream, Action<string> logAction)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _logAction = logAction ?? throw new ArgumentNullException(nameof(logAction));
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var data = Encoding.UTF8.GetString(buffer, offset, count);
        _logAction(data);
        _innerStream.Write(buffer, offset, count);
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
    public override void Flush() => _innerStream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
}
