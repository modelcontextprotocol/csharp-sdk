using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for session-scoped MRTR resource governance — verifying that outgoing message
/// filters can track and limit MRTR round trips per session.
/// </summary>
public class MrtrSessionLimitTests : ClientServerTestBase
{
    /// <summary>
    /// Tracks the number of pending MRTR flows per session. Incremented when an IncompleteResult
    /// is sent (outgoing filter), decremented when a retry with requestState arrives (incoming filter).
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _pendingFlowsPerSession = new();

    /// <summary>
    /// Records every (sessionId, pendingCount) observation from the outgoing filter,
    /// so the test can verify the tracking was correct.
    /// </summary>
    private readonly ConcurrentBag<(string SessionId, int PendingCount)> _observations = [];

    private readonly ServerMessageTracker _tracker = new();

    /// <summary>
    /// Maximum allowed concurrent MRTR flows per session. If exceeded, the outgoing filter
    /// replaces the IncompleteResult with an error response.
    /// </summary>
    private int _maxFlowsPerSession = int.MaxValue;

    /// <summary>
    /// Counts how many IncompleteResults were blocked by the per-session limit.
    /// </summary>
    private int _blockedFlowCount;

    public MrtrSessionLimitTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
            _tracker.AddFilters(options.Filters.Message);

            // Outgoing filter: detect IncompleteResult responses and track per session.
            options.Filters.Message.OutgoingFilters.Add(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcResponse response &&
                    response.Result is JsonObject resultObj &&
                    resultObj.TryGetPropertyValue("result_type", out var resultTypeNode) &&
                    resultTypeNode?.GetValue<string>() is "incomplete")
                {
                    var sessionId = context.Server.SessionId ?? "unknown";
                    var newCount = _pendingFlowsPerSession.AddOrUpdate(sessionId, 1, (_, c) => c + 1);
                    _observations.Add((sessionId, newCount));

                    // Enforce per-session limit: if exceeded, replace the IncompleteResult
                    // with a JSON-RPC error. This prevents the client from receiving the
                    // IncompleteResult and starting another retry cycle.
                    if (newCount > _maxFlowsPerSession)
                    {
                        // Undo the increment since we're blocking this flow.
                        _pendingFlowsPerSession.AddOrUpdate(sessionId, 0, (_, c) => Math.Max(0, c - 1));
                        Interlocked.Increment(ref _blockedFlowCount);

                        // Replace the outgoing message with a JSON-RPC error.
                        context.JsonRpcMessage = new JsonRpcError
                        {
                            Id = response.Id,
                            Error = new JsonRpcErrorDetail
                            {
                                Code = (int)McpErrorCode.InvalidRequest,
                                Message = $"Too many pending MRTR flows for this session (limit: {_maxFlowsPerSession}).",
                            }
                        };
                    }
                }

                await next(context, cancellationToken);
            });

            // Incoming filter: detect retries (requests with requestState) and decrement.
            options.Filters.Message.IncomingFilters.Add(next => async (context, cancellationToken) =>
            {
                if (context.JsonRpcMessage is JsonRpcRequest request &&
                    request.Params is JsonObject paramsObj &&
                    paramsObj.TryGetPropertyValue("requestState", out var stateNode) &&
                    stateNode is not null)
                {
                    var sessionId = context.Server.SessionId ?? "unknown";
                    _pendingFlowsPerSession.AddOrUpdate(sessionId, 0, (_, c) => Math.Max(0, c - 1));
                }

                await next(context, cancellationToken);
            });
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                async (string message, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.ElicitAsync(new ElicitRequestParams
                    {
                        Message = message,
                        RequestedSchema = new()
                    }, ct);

                    return $"{result.Action}";
                },
                new McpServerToolCreateOptions
                {
                    Name = "elicit-tool",
                    Description = "A tool that requests elicitation"
                }),
        ]);
    }

    [Fact]
    public async Task OutgoingFilter_TracksIncompleteResultsPerSession()
    {
        // Verify that an outgoing message filter can observe IncompleteResult responses
        // and track the pending MRTR flow count per session using context.Server.SessionId.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Call the tool — triggers one MRTR round-trip.
        var result = await client.CallToolAsync("elicit-tool",
            new Dictionary<string, object?> { ["message"] = "confirm?" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("accept", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);

        // Verify the filter observed exactly one IncompleteResult and tracked it.
        Assert.Single(_observations);
        var (sessionId, pendingCount) = _observations.First();
        Assert.NotNull(sessionId);
        Assert.Equal(1, pendingCount);

        // After the retry completed, the count should be back to 0.
        Assert.Equal(0, _pendingFlowsPerSession.GetValueOrDefault(sessionId));

        _tracker.AssertMrtrUsed();
    }

    [Fact]
    public async Task OutgoingFilter_CanEnforcePerSessionMrtrLimit()
    {
        // Verify that an outgoing message filter can enforce a per-session MRTR flow limit
        // by replacing the IncompleteResult with a JSON-RPC error when the limit is exceeded.
        // Set the limit to 0 so the very first MRTR flow is blocked.
        _maxFlowsPerSession = 0;

        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The tool call should fail because the outgoing filter blocks the IncompleteResult.
        var ex = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await client.CallToolAsync("elicit-tool",
                new Dictionary<string, object?> { ["message"] = "confirm?" },
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Too many pending MRTR flows", ex.Message);
        Assert.Equal(1, _blockedFlowCount);
    }
}
