using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Tests for the MRTR server API - IsMrtrSupported, InputRequiredException,
/// and client auto-retry of incomplete results.
/// </summary>
public class MrtrInputRequiredExceptionTests : ClientServerTestBase
{
    private readonly ServerMessageTracker _messageTracker = new();

    public MrtrInputRequiredExceptionTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ProtocolVersion = "2026-07-28";
            _messageTracker.AddFilters(options.Filters.Message);
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new InputRequiredException(requestState: "should-not-work");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete",
                    Description = "Tool that always throws InputRequiredException"
                }),
        ]);
    }

    [Fact]
    public async Task InputRequiredException_WithoutInputRequests_ExhaustsRetries()
    {
        StartServer();
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The always-incomplete tool throws InputRequiredException with only requestState
        // and no inputRequests. The client has nothing to dispatch, so it keeps retrying
        // with the same requestState until the retry budget is exhausted.
        var exception = await Assert.ThrowsAsync<McpException>(() =>
            client.CallToolAsync("always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("more than", exception.Message);
    }
}

/// <summary>
/// Companion to <see cref="MrtrInputRequiredExceptionTests"/> covering a native (MRTR-capable) round-trip where the
/// server RETURNS an <see cref="InputRequiredResult"/> through the alternate result path
/// (<see cref="ResultOrAlternate{TResult}"/>) rather than throwing <see cref="InputRequiredException"/>. The MRTR
/// client drives the round-trip and receives the final result.
/// </summary>
public class MrtrReturnedInputRequiredResultNativeTests : ClientServerTestBase
{
    private static readonly JsonTypeInfo<InputRequiredResult> s_inputRequiredResultTypeInfo =
        (JsonTypeInfo<InputRequiredResult>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(InputRequiredResult));

    private int _attempt;

    public MrtrReturnedInputRequiredResultNativeTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

#pragma warning disable MCPEXP002 // exercises the experimental CallToolWithAlternateHandler/ResultOrAlternate seam
    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ProtocolVersion = "2026-07-28";

            options.Handlers.CallToolWithAlternateHandler = (context, cancellationToken) =>
            {
                Interlocked.Increment(ref _attempt);

                // Retry round: the MRTR client re-sent the request with its responses and our requestState.
                if (context.Params?.RequestState is not null)
                {
                    return new ValueTask<ResultOrAlternate<CallToolResult>>(new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = "resolved" }],
                    });
                }

                // First round: RETURN an InputRequiredResult through the alternate path. An MRTR client
                // understands it natively and drives the round-trip.
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
    public async Task ReturnedInputRequiredResult_MrtrClient_RoundTripsToFinalResult()
    {
        StartServer();

        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new() },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
            new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync(
            "return-form",
            cancellationToken: TestContext.Current.CancellationToken);

        // Two handler invocations: initial (returned InputRequiredResult) + client-driven retry (final result).
        Assert.Equal(2, _attempt);
        var content = Assert.Single(result.Content);
        Assert.Equal("resolved", Assert.IsType<TextContentBlock>(content).Text);
    }
}
