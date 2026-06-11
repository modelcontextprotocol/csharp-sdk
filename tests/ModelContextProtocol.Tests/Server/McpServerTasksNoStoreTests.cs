using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Runtime.InteropServices;

#pragma warning disable MCPEXP001

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Pins the behavior when a client signals the SEP-2575 tasks opt-in via <c>_meta</c> but the server
/// has neither an <see cref="McpServerOptions.TaskStore"/> nor a <see cref="McpServerHandlers.CallToolWithTaskHandler"/>
/// configured. The expected behavior is a silent synchronous fallback: the server returns the normal
/// <see cref="CallToolResult"/> with no <c>Task</c> envelope and no exception.
/// </summary>
public class McpServerTasksNoStoreTests : ClientServerTestBase
{
    public McpServerTasksNoStoreTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
#if !NET
        Assert.SkipWhen(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "https://github.com/modelcontextprotocol/csharp-sdk/issues/587");
#endif
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        // Intentionally do NOT configure TaskStore or CallToolWithTaskHandler.
        mcpServerBuilder.WithTools<NoStoreTools>();
    }

    [Fact]
    public async Task ClientOptIn_NoTaskStore_FallsBackToSyncResult()
    {
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        // CallToolRawAsync always writes the SEP-2575 tasks opt-in into _meta.
        var augmented = await client.CallToolRawAsync(
            new CallToolRequestParams { Name = "sync-tool" }, ct);

        // With no task store configured, the server must complete synchronously and return
        // the standard CallToolResult — not a task envelope.
        Assert.False(augmented.IsTask);
        Assert.NotNull(augmented.Result);
        Assert.Equal("sync result", Assert.IsType<TextContentBlock>(augmented.Result!.Content[0]).Text);
    }

    [Fact]
    public async Task ClientOptIn_NoTaskStore_CallToolAsync_StillReturnsResult()
    {
        // CallToolAsync (the higher-level convenience) must also work in the no-store case,
        // delegating to the underlying sync path without throwing.
        await using var client = await CreateMcpClientForServer();
        var ct = TestContext.Current.CancellationToken;

        var result = await client.CallToolAsync(
            new CallToolRequestParams { Name = "sync-tool" }, ct);

        Assert.NotNull(result);
        Assert.Equal("sync result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }

    [McpServerToolType]
    private sealed class NoStoreTools
    {
        [McpServerTool(Name = "sync-tool"), System.ComponentModel.Description("A plain sync tool")]
        public static string SyncTool() => "sync result";
    }
}
