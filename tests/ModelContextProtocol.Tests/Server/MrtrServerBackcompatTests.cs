using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
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

/// <summary>
/// Companion to <see cref="MrtrServerBackcompatTests"/> covering the other way a handler can surface an
/// input-required result to a non-MRTR client: by RETURNING an <see cref="InputRequiredResult"/> through the
/// alternate result path (<see cref="ResultOrAlternate{TResult}"/>) instead of throwing
/// <see cref="InputRequiredException"/>. The legacy backcompat resolver must normalize both forms so a non-MRTR
/// stateful client gets the same server-side resolution either way.
/// </summary>
public class MrtrReturnedInputRequiredResultBackcompatTests : ClientServerTestBase
{
    private static readonly JsonTypeInfo<InputRequiredResult> s_inputRequiredResultTypeInfo =
        (JsonTypeInfo<InputRequiredResult>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(InputRequiredResult));

    private int _attempt;

    public MrtrReturnedInputRequiredResultBackcompatTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateHandler/ResultOrAlternate seam
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.Services.Configure<McpServerOptions>(options =>
        {
            options.Handlers.CallToolWithAlternateHandler = (context, cancellationToken) =>
            {
                Interlocked.Increment(ref _attempt);

                // Retry round: the backcompat resolver re-invoked us with the client's responses.
                if (context.Params?.RequestState is not null)
                {
                    return new ValueTask<ResultOrAlternate<CallToolResult>>(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "resolved" }],
                    });
                }

                // First round: RETURN an InputRequiredResult through the alternate path rather than throwing.
                var inputRequired = new InputRequiredResult
                {
                    InputRequests = new Dictionary<string, InputRequest>
                    {
                        ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                        {
                            Message = "need-input",
                            RequestedSchema = new(),
                        }),
                    },
                    RequestState = "round1",
                };

                return new ValueTask<ResultOrAlternate<CallToolResult>>(
                    ResultOrAlternate<CallToolResult>.FromAlternate(inputRequired, s_inputRequiredResultTypeInfo));
            };
        });
    }
#pragma warning restore MCPEXP002

    [Fact]
    public async Task ReturnedInputRequiredResult_NonMrtrStatefulClient_ResolvedServerSide()
    {
        StartServer();

        // Non-MRTR client → server falls into the legacy backcompat resolver path, which must handle a
        // RETURNED InputRequiredResult exactly like a thrown InputRequiredException.
        var clientOptions = new McpClientOptions
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new ClientCapabilities { Elicitation = new() },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync(
            "return-form",
            cancellationToken: TestContext.Current.CancellationToken);

        // Two handler invocations: initial (returned InputRequiredResult) + retry (final result).
        Assert.Equal(2, _attempt);
        var content = Assert.Single(result.Content);
        Assert.Equal("resolved", Assert.IsType<TextContentBlock>(content).Text);
    }
}
