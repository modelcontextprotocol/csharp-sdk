using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Integration tests for the low-level MRTR API where tool handlers directly throw
/// <see cref="IncompleteResultException"/> and manage request state themselves.
/// </summary>
public class McpClientMrtrLowLevelTests : ClientServerTestBase
{
    public McpClientMrtrLowLevelTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper, startServer: false)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        services.Configure<McpServerOptions>(options =>
        {
            options.ExperimentalProtocolVersion = "2026-06-XX";
        });

        mcpServerBuilder.WithTools([
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var elicitResult = inputResponses["user_input"].ElicitationResult;
                        return $"completed:{elicitResult?.Action}:{elicitResult?.Content?.FirstOrDefault().Value}";
                    }

                    if (!server.IsMrtrSupported)
                    {
                        return "fallback:MRTR not supported";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["user_input"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Please provide input",
                                RequestedSchema = new()
                            })
                        },
                        requestState: "state-v1");
                },
                new McpServerToolCreateOptions
                {
                    Name = "lowlevel-elicit",
                    Description = "Low-level tool that elicits via IncompleteResultException"
                }),
            McpServerTool.Create(
                static string (McpServer server, RequestContext<CallToolRequestParams> context) =>
                {
                    var requestState = context.Params!.RequestState;
                    var inputResponses = context.Params!.InputResponses;

                    if (requestState is not null && inputResponses is not null)
                    {
                        var samplingResult = inputResponses["llm_request"].SamplingResult;
                        var text = samplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        return $"sampled:{text}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["llm_request"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Generate something" }] }],
                                MaxTokens = 50
                            })
                        },
                        requestState: "sampling-state");
                },
                new McpServerToolCreateOptions
                {
                    Name = "lowlevel-sample",
                    Description = "Low-level tool that samples via IncompleteResultException"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    if (context.Params!.RequestState is not null)
                    {
                        return $"resumed:{context.Params!.RequestState}";
                    }

                    throw new IncompleteResultException(requestState: "shedding-load");
                },
                new McpServerToolCreateOptions
                {
                    Name = "loadshed",
                    Description = "Low-level tool that returns requestState only"
                }),
            // A high-level tool that uses SampleAsync (for mixed tests)
            McpServerTool.Create(
                async (string prompt, McpServer server, CancellationToken ct) =>
                {
                    var result = await server.SampleAsync(new CreateMessageRequestParams
                    {
                        Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = prompt }] }],
                        MaxTokens = 100
                    }, ct);

                    return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No response";
                },
                new McpServerToolCreateOptions
                {
                    Name = "highlevel-sample",
                    Description = "High-level tool using SampleAsync"
                }),
            McpServerTool.Create(
                static string (McpServer server) =>
                {
                    throw new IncompleteResultException(requestState: "should-not-work");
                },
                new McpServerToolCreateOptions
                {
                    Name = "always-incomplete",
                    Description = "Tool that always throws IncompleteResultException"
                }),
            McpServerTool.Create(
                static string (RequestContext<CallToolRequestParams> context) =>
                {
                    var inputResponses = context.Params!.InputResponses;

                    if (inputResponses is not null &&
                        inputResponses.TryGetValue("elicit", out var elicitResponse) &&
                        inputResponses.TryGetValue("sample", out var sampleResponse))
                    {
                        var action = elicitResponse.ElicitationResult?.Action;
                        var text = sampleResponse.SamplingResult?.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
                        return $"multi:{action}:{text}";
                    }

                    throw new IncompleteResultException(
                        inputRequests: new Dictionary<string, InputRequest>
                        {
                            ["elicit"] = InputRequest.ForElicitation(new ElicitRequestParams
                            {
                                Message = "Confirm?",
                                RequestedSchema = new()
                            }),
                            ["sample"] = InputRequest.ForSampling(new CreateMessageRequestParams
                            {
                                Messages = [new SamplingMessage { Role = Role.User, Content = [new TextContentBlock { Text = "Summarize" }] }],
                                MaxTokens = 50
                            })
                        },
                        requestState: "multi-input");
                },
                new McpServerToolCreateOptions
                {
                    Name = "lowlevel-multi-input",
                    Description = "Low-level tool with multiple InputRequests in one IncompleteResult"
                }),
        ]);
    }

    [Fact]
    public async Task LowLevel_ClientAutoRetries_ElicitationIncompleteResult()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"user-response\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("lowlevel-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("completed:accept:user-response", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task LowLevel_ClientAutoRetries_SamplingIncompleteResult()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "LLM output" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("lowlevel-sample",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("sampled:LLM output", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task LowLevel_ClientAutoRetries_RequestStateOnlyResponse()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("loadshed",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("resumed:shedding-load", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task IsMrtrSupported_ReturnsTrue_WhenBothExperimental()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult { Action = "ok" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The lowlevel-elicit tool checks IsMrtrSupported and throws IncompleteResultException
        // This will only work if IsMrtrSupported is true
        var result = await client.CallToolAsync("lowlevel-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        // If IsMrtrSupported was false, it would return "fallback:MRTR not supported"
        var content = Assert.Single(result.Content);
        Assert.StartsWith("completed:", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task IsMrtrSupported_ReturnsFalse_WhenClientNotExperimental()
    {
        StartServer();
        // Client does NOT set ExperimentalProtocolVersion
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The lowlevel-elicit tool checks IsMrtrSupported and returns a fallback message
        var result = await client.CallToolAsync("lowlevel-elicit",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("fallback:MRTR not supported", Assert.IsType<TextContentBlock>(content).Text);
    }

    [Fact]
    public async Task MixedHighAndLowLevelTools_WorkInSameSession()
    {
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            var text = request?.Messages[^1].Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = $"Sampled: {text}" }],
                Model = "test-model"
            });
        };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "confirm",
                Content = new Dictionary<string, JsonElement>
                {
                    ["data"] = JsonDocument.Parse("\"elicited\"").RootElement.Clone()
                }
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        // Call the high-level sampling tool
        var samplingResult = await client.CallToolAsync("highlevel-sample",
            new Dictionary<string, object?> { ["prompt"] = "test prompt" },
            cancellationToken: TestContext.Current.CancellationToken);
        var samplingContent = Assert.Single(samplingResult.Content);
        Assert.Equal("Sampled: test prompt", Assert.IsType<TextContentBlock>(samplingContent).Text);

        // Call the low-level elicitation tool in the same session
        var elicitResult = await client.CallToolAsync("lowlevel-elicit",
            cancellationToken: TestContext.Current.CancellationToken);
        var elicitContent = Assert.Single(elicitResult.Content);
        Assert.Equal("completed:confirm:elicited", Assert.IsType<TextContentBlock>(elicitContent).Text);
    }

    [Fact]
    public async Task LowLevel_IncompleteResultException_WithoutExperimental_ReturnsError()
    {
        StartServer();
        // Client does NOT set ExperimentalProtocolVersion
        var clientOptions = new McpClientOptions();

        await using var client = await CreateMcpClientForServer(clientOptions);

        // The always-incomplete tool throws IncompleteResultException with only requestState
        // and no inputRequests. Without MRTR negotiated, the backcompat layer can't resolve
        // the request (no inputRequests to dispatch), so it wraps it in an error.
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            client.CallToolAsync("always-incomplete",
                cancellationToken: TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("without input requests", exception.Message);
    }

    [Fact]
    public async Task LowLevel_MultipleInputRequests_ClientResolvesBothConcurrently()
    {
        // Tool throws IncompleteResultException with multiple InputRequests in a single
        // IncompleteResult. The MRTR client resolves both via its registered handlers.
        StartServer();
        var clientOptions = new McpClientOptions { ExperimentalProtocolVersion = "2026-06-XX" };
        clientOptions.Handlers.ElicitationHandler = (request, ct) =>
        {
            return new ValueTask<ElicitResult>(new ElicitResult
            {
                Action = "accept",
                Content = new Dictionary<string, JsonElement>
                {
                    ["answer"] = JsonDocument.Parse("\"yes\"").RootElement.Clone()
                }
            });
        };
        clientOptions.Handlers.SamplingHandler = (request, progress, ct) =>
        {
            return new ValueTask<CreateMessageResult>(new CreateMessageResult
            {
                Content = [new TextContentBlock { Text = "LLM output" }],
                Model = "test-model"
            });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);

        var result = await client.CallToolAsync("lowlevel-multi-input",
            cancellationToken: TestContext.Current.CancellationToken);

        var content = Assert.Single(result.Content);
        Assert.Equal("multi:accept:LLM output", Assert.IsType<TextContentBlock>(content).Text);
    }
}
