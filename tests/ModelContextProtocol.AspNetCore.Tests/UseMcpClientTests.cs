using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
#pragma warning disable MCP5002
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

namespace ModelContextProtocol.AspNetCore.Tests;

public class UseMcpClientTests : KestrelInMemoryTest
{
    public UseMcpClientTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    private async Task<WebApplication> StartServerAsync(Action<WebApplication>? configureApp = null)
    {
        IMcpServerBuilder builder = Builder.Services.AddMcpServer(options =>
        {
            options.Capabilities = new ServerCapabilities
            {
                Tools = new(),
                Resources = new(),
                Prompts = new(),
            };
            options.ServerInstructions = "This is a test server with only stub functionality";
            options.Handlers = new()
            {
                ListToolsHandler = async (request, cancellationToken) =>
                {
                    return new ListToolsResult
                    {
                        Tools =
                        [
                            new Tool
                            {
                                Name = "echo",
                                Description = "Echoes the input back to the client.",
                                InputSchema = JsonElement.Parse("""
                                    {
                                        "type": "object",
                                        "properties": {
                                            "message": {
                                                "type": "string",
                                                "description": "The input to echo back."
                                            }
                                        },
                                        "required": ["message"]
                                    }
                                    """),
                            },
                            new Tool
                            {
                                Name = "echoSessionId",
                                Description = "Echoes the session id back to the client.",
                                InputSchema = JsonElement.Parse("""
                                    {
                                        "type": "object"
                                    }
                                    """),
                            },
                            new Tool
                            {
                                Name = "sampleLLM",
                                Description = "Samples from an LLM using MCP's sampling feature.",
                                InputSchema = JsonElement.Parse("""
                                    {
                                        "type": "object",
                                        "properties": {
                                            "prompt": {
                                                "type": "string",
                                                "description": "The prompt to send to the LLM"
                                            },
                                            "maxTokens": {
                                                "type": "number",
                                                "description": "Maximum number of tokens to generate"
                                            }
                                        },
                                        "required": ["prompt", "maxTokens"]
                                    }
                                    """),
                            }
                        ]
                    };
                },
                CallToolHandler = async (request, cancellationToken) =>
                {
                    if (request.Params is null)
                    {
                        throw new McpProtocolException("Missing required parameter 'name'", McpErrorCode.InvalidParams);
                    }
                    if (request.Params.Name == "echo")
                    {
                        if (request.Params.Arguments is null || !request.Params.Arguments.TryGetValue("message", out var message))
                        {
                            throw new McpProtocolException("Missing required argument 'message'", McpErrorCode.InvalidParams);
                        }
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = $"Echo: {message}" }]
                        };
                    }
                    else if (request.Params.Name == "echoSessionId")
                    {
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = request.Server.SessionId ?? string.Empty }]
                        };
                    }
                    else if (request.Params.Name == "sampleLLM")
                    {
                        if (request.Params.Arguments is null ||
                            !request.Params.Arguments.TryGetValue("prompt", out var prompt) ||
                            !request.Params.Arguments.TryGetValue("maxTokens", out var maxTokens))
                        {
                            throw new McpProtocolException("Missing required arguments 'prompt' and 'maxTokens'", McpErrorCode.InvalidParams);
                        }
                        // Simple mock response for sampleLLM
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = "LLM sampling result: Test response" }]
                        };
                    }
                    else
                    {
                        throw new McpProtocolException($"Unknown tool: '{request.Params.Name}'", McpErrorCode.InvalidParams);
                    }
                }
            };
        })
        .WithHttpTransport();

        var app = Builder.Build();
        configureApp?.Invoke(app);
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    /// <summary>
    /// Captures the arguments received by the leaf mock IChatClient.
    /// </summary>
    private sealed class LeafChatClientState
    {
        public ChatOptions? CapturedOptions { get; set; }
        public List<IEnumerable<ChatMessage>> CapturedMessages { get; set; } = [];
        public int CallCount { get; set; }
        public void Clear()
        {
            CapturedOptions = null;
            CapturedMessages.Clear();
            CallCount = 0;
        }
    }

    private IChatClient CreateTestChatClient(out LeafChatClientState leafClientState, Action<HostedMcpServerTool, HttpClientTransportOptions>? configureTransportOptions = null)
    {
        var state = new LeafChatClientState();

        var mockInnerClient = new Mock<IChatClient>();
        mockInnerClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), 
                It.IsAny<ChatOptions>(), 
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken ct) => 
                GetStreamingResponseAsync(messages, options, ct).ToChatResponseAsync(ct));

        mockInnerClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), 
                It.IsAny<ChatOptions>(), 
                It.IsAny<CancellationToken>()))
            .Returns(GetStreamingResponseAsync);

        leafClientState = state;
        return mockInnerClient.Object.AsBuilder()
            .UseMcpClient(HttpClient, LoggerFactory, configureTransportOptions)
            // Placement is important, must be after UseMcpClient, otherwise, UseFunctionInvocation won't see the MCP tools.
            .UseFunctionInvocation() 
            .Build();

        async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, 
            ChatOptions? options, 
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            state.CapturedOptions = options;
            state.CapturedMessages.Add(messages);

            // First call: request to invoke the echo tool
            if (state.CallCount++ == 0 && options?.Tools is { Count: > 0 } tools)
            {
                Assert.Contains(tools, t => t.Name == "echo");
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_123", "echo", new Dictionary<string, object?> { ["message"] = "test message" })
                ]);
            }
            else
            {
                // Subsequent calls: return final response
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Final response");
            }
        }
    }

    private static void AssertLeafClientMessagesWithInvocation(List<IEnumerable<ChatMessage>> capturedMessages)
    {
        Assert.Equal(2, capturedMessages.Count);
        var firstCall = capturedMessages[0];
        var msg = Assert.Single(firstCall);
        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Equal("Test message", msg.Text);

        var secondCall = capturedMessages[1].ToList();
        Assert.Equal(3, secondCall.Count);
        Assert.Equal(ChatRole.User, secondCall[0].Role);
        Assert.Equal("Test message", secondCall[0].Text);

        Assert.Equal(ChatRole.Assistant, secondCall[1].Role);
        var functionCall = Assert.IsType<FunctionCallContent>(Assert.Single(secondCall[1].Contents));
        Assert.Equal("call_123", functionCall.CallId);
        Assert.Equal("echo", functionCall.Name);

        Assert.Equal(ChatRole.Tool, secondCall[2].Role);
        var functionResult = Assert.IsType<FunctionResultContent>(Assert.Single(secondCall[2].Contents));
        Assert.Equal("call_123", functionResult.CallId);
        Assert.Contains("Echo: test message", functionResult.Result?.ToString());
    }

    private static void AssertResponseWithInvocation(ChatResponse response)
    {
        Assert.NotNull(response);
        Assert.Equal(3, response.Messages.Count);
        
        Assert.Equal(ChatRole.Assistant, response.Messages[0].Role);
        Assert.Single(response.Messages[0].Contents);
        Assert.IsType<FunctionCallContent>(response.Messages[0].Contents[0]);
        
        Assert.Equal(ChatRole.Tool, response.Messages[1].Role);
        Assert.Single(response.Messages[1].Contents);
        Assert.IsType<FunctionResultContent>(response.Messages[1].Contents[0]);
        
        Assert.Equal(ChatRole.Assistant, response.Messages[2].Role);
        Assert.Equal("Final response", response.Messages[2].Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UseMcpClient_ShouldProduceTools(bool streaming, bool useUrl)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = useUrl ? 
            new HostedMcpServerTool("serverName", HttpClient.BaseAddress!) : 
            new HostedMcpServerTool("serverName", HttpClient.BaseAddress!.ToString());
        mcpTool.ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire;
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) : 
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(3, toolNames.Count);
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_DoesNotConflictWithRegularTools(bool streaming)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var regularTool = AIFunctionFactory.Create(() => "regular tool result", "regularTool");
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!.ToString())
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions
        {
            Tools = [regularTool, mcpTool]
        };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) : 
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(4, toolNames.Count);
        Assert.Contains("regularTool", toolNames);
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
    }

    public static IEnumerable<object?[]> UseMcpClient_ApprovalMode_TestData()
    {
        string[] allToolNames = ["echo", "echoSessionId", "sampleLLM"];
        foreach (var streaming in new[] { false, true })
        {
            yield return new object?[] { streaming, new HostedMcpServerToolNeverRequireApprovalMode(), (string[])[], allToolNames };
            yield return new object?[] { streaming, new HostedMcpServerToolAlwaysRequireApprovalMode(), allToolNames, (string[])[] };
            yield return new object?[] { streaming, null, allToolNames, (string[])[] };
            // Specific mode with empty lists - all tools should default to requiring approval.
            yield return new object?[] { streaming, new HostedMcpServerToolRequireSpecificApprovalMode([], []), allToolNames, (string[])[] };
            // Specific mode with one tool always requiring approval - the other two should default to requiring approval.
            yield return new object?[] { streaming, new HostedMcpServerToolRequireSpecificApprovalMode(["echo"], []), allToolNames, (string[])[] };
            // Specific mode with one tool never requiring approval - the other two should default to requiring approval.
            yield return new object?[] { streaming, new HostedMcpServerToolRequireSpecificApprovalMode([], ["echo"]), (string[])["echoSessionId", "sampleLLM"], (string[])["echo"] };
        }
    }

    [Theory]
    [MemberData(nameof(UseMcpClient_ApprovalMode_TestData))]
    public async Task UseMcpClient_ApprovalMode(bool streaming, HostedMcpServerToolApprovalMode? approvalMode, string[] expectedApprovalRequiredAIFunctions, string[] expectedNormalAIFunctions)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = approvalMode
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        Assert.Equal(3, leafClientState.CapturedOptions.Tools.Count);

        var toolsRequiringApproval = leafClientState.CapturedOptions.Tools
            .Where(t => t is ApprovalRequiredAIFunction).Select(t => t.Name);
        var toolsNotRequiringApproval = leafClientState.CapturedOptions.Tools
            .Where(t => t is not ApprovalRequiredAIFunction).Select(t => t.Name);

        Assert.Equivalent(expectedApprovalRequiredAIFunctions, toolsRequiringApproval);
        Assert.Equivalent(expectedNormalAIFunctions, toolsNotRequiringApproval);
    }

    public static IEnumerable<object?[]> UseMcpClient_HandleFunctionApprovalRequest_TestData()
    {
        foreach (var streaming in new[] { false, true })
        {
            // Approval modes that will cause function approval requests
            yield return new object?[] { streaming, null };
            yield return new object?[] { streaming, HostedMcpServerToolApprovalMode.AlwaysRequire };
            yield return new object?[] { streaming, HostedMcpServerToolApprovalMode.RequireSpecific(["echo"], null) };
        }
    }

    [Theory]
    [MemberData(nameof(UseMcpClient_HandleFunctionApprovalRequest_TestData))]
    public async Task UseMcpClient_HandleFunctionApprovalRequest(bool streaming, HostedMcpServerToolApprovalMode? approvalMode)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = approvalMode
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        List<ChatMessage> chatHistory = [];
        chatHistory.Add(new ChatMessage(ChatRole.User, "Test message"));
        var response = streaming ? 
            await sut.GetStreamingResponseAsync(chatHistory, options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync(chatHistory, options, TestContext.Current.CancellationToken);

        chatHistory.AddRange(response.Messages);
        var approvalRequest = Assert.Single(response.Messages.SelectMany(m => m.Contents).OfType<FunctionApprovalRequestContent>());
        chatHistory.Add(new ChatMessage(ChatRole.User, [approvalRequest.CreateResponse(true)]));

        response = streaming ?
            await sut.GetStreamingResponseAsync(chatHistory, options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync(chatHistory, options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
    }

    [Theory]
    [InlineData(false, null, (string[])["echo", "echoSessionId", "sampleLLM"])]
    [InlineData(true, null, (string[])["echo", "echoSessionId", "sampleLLM"])]
    [InlineData(false, (string[])["echo"], (string[])["echo"])]
    [InlineData(true, (string[])["echo"], (string[])["echo"])]
    [InlineData(false, (string[])[], (string[])[])]
    [InlineData(true, (string[])[], (string[])[])]
    public async Task UseMcpClient_AllowedTools_FiltersCorrectly(bool streaming, string[]? allowedTools, string[] expectedTools)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            AllowedTools = allowedTools,
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(expectedTools.Length, toolNames.Count);
        Assert.Equivalent(expectedTools, toolNames);
        
        if (expectedTools.Contains("echo"))
        {
            AssertResponseWithInvocation(response);
            AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        }
        else
        {
            var responseMsg = Assert.Single(response.Messages);
            Assert.Equal(ChatRole.Assistant, responseMsg.Role);
            Assert.Equal("Final response", responseMsg.Text);

            Assert.Single(leafClientState.CapturedMessages);
            var firstCall = leafClientState.CapturedMessages[0];
            var leafClientMessage = Assert.Single(firstCall);
            Assert.Equal(ChatRole.User, leafClientMessage.Role);
            Assert.Equal("Test message", leafClientMessage.Text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_AuthorizationTokenHeaderFlowsCorrectly(bool streaming)
    {
        // Arrange
        const string testToken = "test-bearer-token-12345";
        bool authReceivedForInitialize = false;
        bool authReceivedForNotificationsInitialized = false;
        bool authReceivedForToolsList = false;
        bool authReceivedForToolsCall = false;

        await using var _ = await StartServerAsync(
            configureApp: app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Method == "POST" &&
                        context.Request.Headers.TryGetValue("Authorization", out var authHeader))
                    {
                        Assert.Equal($"Bearer {testToken}", authHeader.ToString());

                        context.Request.EnableBuffering();
                        JsonRpcRequest? rpcRequest = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                            context.Request.Body, 
                            McpJsonUtilities.DefaultOptions, 
                            context.RequestAborted);
                        context.Request.Body.Position = 0;
                        Assert.NotNull(rpcRequest);
                        
                        switch (rpcRequest.Method)
                        {
                            case "initialize":
                                authReceivedForInitialize = true;
                                break;
                            case "notifications/initialized":
                                authReceivedForNotificationsInitialized = true;
                                break;
                            case "tools/list":
                                authReceivedForToolsList = true;
                                break;
                            case "tools/call":
                                authReceivedForToolsCall = true;
                                break;
                        }
                    }
                    await next();
                });
            });
        
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            AuthorizationToken = testToken,
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions
        {
            Tools = [mcpTool]
        };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) : 
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        Assert.True(authReceivedForInitialize, "Authorization header was not captured in initial request");
        Assert.True(authReceivedForNotificationsInitialized, "Authorization header was not captured in notifications/initialized request");
        Assert.True(authReceivedForToolsList, "Authorization header was not captured in tools/list request");
        Assert.True(authReceivedForToolsCall, "Authorization header was not captured in tools/call request");

        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(3, toolNames.Count);
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_CachesClientForSameServerAddress(bool streaming)
    {
        // Arrange
        int initializeCallCount = 0;
        await using var _ = await StartServerAsync(configureApp: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == "POST")
                {
                    context.Request.EnableBuffering();
                    var rpcRequest = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions,
                        context.RequestAborted);
                    context.Request.Body.Position = 0;

                    if (rpcRequest?.Method == "initialize")
                    {
                        initializeCallCount++;
                    }
                }
                await next();
            });
        });
        
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var firstCallToolCount = leafClientState.CapturedOptions.Tools.Count;
        Assert.Equal(3, firstCallToolCount);
        var toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
        Assert.Equal(1, initializeCallCount);

        // Arrange
        leafClientState.Clear();

        // Act
        var secondResponse = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(secondResponse);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
        Assert.NotNull(leafClientState.CapturedOptions);
        Assert.NotNull(leafClientState.CapturedOptions.Tools);
        var secondCallToolCount = leafClientState.CapturedOptions.Tools.Count;
        Assert.Equal(3, secondCallToolCount);
        Assert.Equal(firstCallToolCount, secondCallToolCount);
        toolNames = leafClientState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
        Assert.True(initializeCallCount == 1, "Initialize should not be called more than once because the MCP client is cached.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_RetriesWhenSessionRevokedByServer(bool streaming)
    {
        // Arrange
        string? firstSessionId = null;
        string? secondSessionId = null;
        
        await using var app = await StartServerAsync(
            configureApp: app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Method == "POST")
                    {
                        context.Request.EnableBuffering();
                        var rpcRequest = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                            context.Request.Body, 
                            McpJsonUtilities.DefaultOptions);
                        context.Request.Body.Position = 0;
                        
                        if (rpcRequest?.Method == "tools/call" && context.Request.Headers.TryGetValue("Mcp-Session-Id", out var sessionIdHeader))
                        {
                            var sessionId = sessionIdHeader.ToString();
                            
                            if (firstSessionId == null)
                            {
                                // First tool call - capture session and return 404 to revoke it
                                firstSessionId = sessionId;
                                context.Response.StatusCode = StatusCodes.Status404NotFound;
                                return;
                            }
                            else
                            {
                                // Second tool call - capture session and let it succeed
                                secondSessionId = sessionId;
                            }
                        }
                    }
                    await next();
                });
            });
        
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions { Tools = [mcpTool] };
    
        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);
    
        // Assert
        Assert.NotNull(firstSessionId);
        Assert.NotNull(secondSessionId);
        Assert.NotEqual(firstSessionId, secondSessionId);
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_RetriesOnServerError(bool streaming)
    {
        int toolCallCount = 0;
        await using var app = await StartServerAsync(configureApp: app =>
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Method == "POST")
                {
                    context.Request.EnableBuffering();
                    var rpcRequest = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                        context.Request.Body,
                        McpJsonUtilities.DefaultOptions,
                        context.RequestAborted);
                    context.Request.Body.Position = 0;

                    if (rpcRequest?.Method == "tools/call" && ++toolCallCount == 1)
                    {
                        throw new Exception("Simulated server error.");
                    }
                }
                await next();
            });
        });

        using IChatClient sut = CreateTestChatClient(out var leafClientState);

        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ?
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, toolCallCount);
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_ConfigureTransportOptions_CallbackIsInvoked(bool streaming)
    {
        // Arrange
        HostedMcpServerTool? capturedTool = null;
        HttpClientTransportOptions? capturedTransportOptions = null;
        await using var _ = await StartServerAsync();
        
        using IChatClient sut = CreateTestChatClient(out var leafClientState, (tool, transportOptions) =>
        {
            capturedTool = tool;
            capturedTransportOptions = transportOptions;
        });

        var mcpTool = new HostedMcpServerTool("serverName", HttpClient.BaseAddress!)
        {
            ApprovalMode = HostedMcpServerToolApprovalMode.NeverRequire,
            AuthorizationToken = "test-auth-token-123"
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        var response = streaming ? 
            await sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            await sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);

        // Assert
        AssertResponseWithInvocation(response);
        AssertLeafClientMessagesWithInvocation(leafClientState.CapturedMessages);

        Assert.NotNull(capturedTool);
        Assert.Equal("serverName", capturedTool.ServerName);
        Assert.Equal(HttpClient.BaseAddress!.ToString(), capturedTool.ServerAddress);
        Assert.Null(capturedTool.ServerDescription);
        Assert.Null(capturedTool.AuthorizationToken);
        Assert.Null(capturedTool.AllowedTools);
        Assert.Null(capturedTool.ApprovalMode);

        Assert.NotNull(capturedTransportOptions);
        Assert.Equal(HttpClient.BaseAddress, capturedTransportOptions.Endpoint);
        Assert.Equal("serverName", capturedTransportOptions.Name);
        Assert.Equal("Bearer test-auth-token-123", capturedTransportOptions.AdditionalHeaders!["Authorization"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_ThrowsInvalidOperationException_WhenServerAddressIsInvalid(bool streaming)
    {
        // Arrange
        await using var _ = await StartServerAsync();
        using IChatClient sut = CreateTestChatClient(out var leafClientState);
        var mcpTool = new HostedMcpServerTool("serverNameConnector", "test-connector-123");
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => streaming ?
            sut.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken).ToChatResponseAsync(TestContext.Current.CancellationToken) :
            sut.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken));
        Assert.Contains("test-connector-123", exception.Message);
    }
}
