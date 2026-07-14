using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the legacy MRTR backcompat resolver in <c>McpServerImpl.InvokeWithInputRequiredResultHandlingAsync</c>.
/// This path runs only when the client did NOT negotiate MRTR (2026-07-28) and the session is stateful, where
/// the server dispatches each input request to the client via standard JSON-RPC and re-invokes the handler
/// with the merged responses. To exercise it the server must NOT pin a protocol version; the client picks
/// a legacy version during initialize negotiation.
/// </summary>
public class MrtrServerBackcompatTests : ClientServerTestBase
{
    private readonly List<string?> _observedRequestStates = [];
    private int _attempt;

    public MrtrServerBackcompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                (RequestContext<CallToolRequestParams> context) =>
                {
                    var attempt = Interlocked.Increment(ref _attempt);
                    _observedRequestStates.Add(context.Params?.RequestState);

                    return attempt switch
                    {
                        // Round 1: caller has no state; emit one and request elicitation.
                        1 => throw new InputRequiredException(
                            inputRequests: new Dictionary<string, InputRequest>
                            {
                                ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                                {
                                    Message = "round1",
                                    RequestedSchema = new()
                                })
                            },
                            requestState: "round1"),
                        // Round 2: deliberately clear the state by passing requestState: null while still
                        // asking for another elicitation. This exercises the params clone path that
                        // previously preserved the stale "round1" carry-over from round 1's deep clone.
                        2 => throw new InputRequiredException(
                            inputRequests: new Dictionary<string, InputRequest>
                            {
                                ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                                {
                                    Message = "round2",
                                    RequestedSchema = new()
                                })
                            },
                            requestState: null),
                        // Round 3 (final): report what the handler observed so the test can assert it.
                        _ => $"final-state:{context.Params?.RequestState ?? "<null>"}",
                    };
                },
                new McpServerToolCreateOptions
                {
                    Name = "requeststate-transition",
                    Description = "Tool that transitions requestState from set to null across MRTR rounds."
                }),
        ]);
    }

    [Fact]
    public async Task InputRequiredException_TransitioningRequestStateToNull_DoesNotLeakStaleState()
    {
        StartServer();

        // Non-MRTR client → server falls into the legacy backcompat resolver path on InputRequiredException.
        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new ClientCapabilities { Elicitation = new() },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
            new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"ok\"").RootElement,
                },
            });

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync(
            "requeststate-transition",
            cancellationToken: TestContext.Current.CancellationToken);

        // Three attempts: round 1 (no state) → round 2 (state="round1") → round 3 (state=null after fix).
        // Without the fix, the third observed state would erroneously remain "round1" because the deep-clone
        // of the prior request params carried it forward when InputRequiredException.RequestState was null.
        Assert.Equal(3, _observedRequestStates.Count);
        Assert.Null(_observedRequestStates[0]);
        Assert.Equal("round1", _observedRequestStates[1]);
        Assert.Null(_observedRequestStates[2]);

        var content = Assert.Single(result.Content);
        var text = Assert.IsType<TextContentBlock>(content).Text;
        Assert.Equal("final-state:<null>", text);
    }
}
