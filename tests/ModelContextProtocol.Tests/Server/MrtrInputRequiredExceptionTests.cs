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
    private int _inputElicitCallCount;

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
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new InputRequiredException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["confirm"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "always-incomplete",
                                RequestedSchema = new(),
                            }),
                        },
                        requestState: "should-not-work");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete-with-input",
                    Description = "Tool that always requests input"
                }),
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new InputRequiredException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["first"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "first",
                                RequestedSchema = new(),
                            }),
                            ["second"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "second" }] }],
                                MaxTokens = 1,
                            }),
                        },
                        requestState: "two-inputs");
                },
                new McpServerToolCreateOptions
                {
                    Name = "two-inputs",
                    Description = "Tool that requests two inputs"
                }),
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new InputRequiredException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["blocking-first"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "blocking-first" }] }],
                                MaxTokens = 1,
                            }),
                            ["failing-second"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "failing-second",
                                RequestedSchema = new(),
                            }),
                        },
                        requestState: "reversed-two-inputs");
                },
                new McpServerToolCreateOptions
                {
                    Name = "reversed-two-inputs",
                    Description = "Tool that requests a blocking input before a failing input"
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

        Assert.Contains("retry", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10", exception.Message);
    }

    [Fact]
    public async Task InputRequiredException_WithInputRequests_ExhaustsRetriesWithoutExtraResolution()
    {
        StartServer();
        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities { Elicitation = new() },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
        {
            Interlocked.Increment(ref _inputElicitCallCount);
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        await Assert.ThrowsAsync<McpException>(() =>
            client.CallToolAsync(
                "always-incomplete-with-input",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(10, _inputElicitCallCount);
    }

    [Fact]
    public async Task InputRequiredException_InputHandlerFailureCancelsSiblingHandler()
    {
        StartServer();
        var siblingCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new(),
                Sampling = new(),
            },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
            throw new InvalidOperationException("first-input-failed");
        clientOptions.Handlers.SamplingHandler = async (_, _, cancellationToken) =>
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
                return new CreateMessageResult
                {
                    Content = [new TextContentBlock { Text = "unexpected" }],
                    Model = "unexpected",
                };
            }
            catch (OperationCanceledException)
            {
                siblingCancelled.TrySetResult(true);
                throw;
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallToolAsync(
                "two-inputs",
                cancellationToken: TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));

        Assert.Equal("first-input-failed", exception.Message);
        await siblingCancelled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InputRequiredException_FailureAfterBlockingSiblingPreservesFailure()
    {
        StartServer();
        var siblingCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientOptions = new McpClientOptions
        {
            Capabilities = new ClientCapabilities
            {
                Elicitation = new(),
                Sampling = new(),
            },
        };
        clientOptions.Handlers.ElicitationHandler = (_, _) =>
            throw new InvalidOperationException("second-input-failed");
        clientOptions.Handlers.SamplingHandler = async (_, _, cancellationToken) =>
        {
            try
            {
                await gate.Task.WaitAsync(cancellationToken);
                return new CreateMessageResult
                {
                    Content = [new TextContentBlock { Text = "unexpected" }],
                    Model = "unexpected",
                };
            }
            catch (OperationCanceledException)
            {
                siblingCancelled.TrySetResult(true);
                throw;
            }
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallToolAsync(
                "reversed-two-inputs",
                cancellationToken: TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));

        Assert.Equal("second-input-failed", exception.Message);
        await siblingCancelled.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
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
