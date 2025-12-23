using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;
using Moq;
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace ModelContextProtocol.AspNetCore.Tests;

public class UseMcpClientWithTestSseServerTests : LoggedTest, IClassFixture<SseServerWithMockLoggerFixture>
{
    private readonly HttpClientTransportOptions _transportOptions;
    private readonly SseServerWithMockLoggerFixture _fixture;

    public UseMcpClientWithTestSseServerTests(SseServerWithMockLoggerFixture fixture, ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _transportOptions = new HttpClientTransportOptions()
        {
            Endpoint = new("http://localhost:5000/sse"),
            Name = "TestSseServer",
        };

        _fixture = fixture;
        _fixture.Initialize(testOutputHelper, _transportOptions);
    }

    public override void Dispose()
    {
        _fixture.TestCompleted();
        base.Dispose();
    }

    private sealed class CallbackState
    {
        public ChatOptions? CapturedOptions { get; set; }
    }

    private IChatClient CreateTestChatClient(out CallbackState callbackState)
    {
        var state = new CallbackState();

        var mockInnerClient = new Mock<IChatClient>();
        mockInnerClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, opts, ct) => state.CapturedOptions = opts)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Dummy response")]));

        mockInnerClient
            .Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, opts, ct) => state.CapturedOptions = opts)
            .Returns(GetStreamingResponseAsync());

        callbackState = state;
        return mockInnerClient.Object.AsBuilder()
            .UseMcpClient(_fixture.HttpClient, LoggerFactory)
            .Build();

        static async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Dummy response");
        }
    }

    private async Task GetResponseAsync(IChatClient client, ChatOptions options, bool streaming)
    {
        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync("Test message", options, TestContext.Current.CancellationToken))
            { }
        }
        else
        {
            _ = await client.GetResponseAsync("Test message", options, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UseMcpClient_ShouldProduceTools(bool streaming, bool useUrl)
    {
        // Arrange
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var mcpTool = useUrl ? 
            new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint) : 
            new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint.ToString());
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        await GetResponseAsync(sut, options, streaming);

        // Assert
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var toolNames = callbackState.CapturedOptions.Tools.Select(t => t.Name).ToList();
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
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var regularTool = AIFunctionFactory.Create(() => "regular tool result", "RegularTool");
        var mcpTool = new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint);
        var options = new ChatOptions
        {
            Tools =
            [
                regularTool,
                mcpTool
            ]
        };

        // Act
        await GetResponseAsync(sut, options, streaming);

        // Assert
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var toolNames = callbackState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(4, toolNames.Count);
        Assert.Contains("RegularTool", toolNames);
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_AuthorizationTokenHeaderFlowsCorrectly(bool streaming)
    {
        // Arrange
        const string testToken = "test-bearer-token-12345";
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var mcpTool = new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint)
        {
            AuthorizationToken = testToken
        };
        var options = new ChatOptions
        {
            Tools = [mcpTool]
        };

        // Act
        await GetResponseAsync(sut, options, streaming);

        // Assert
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var toolNames = callbackState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(3, toolNames.Count);
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
        // We set TestSseServer to log IHeaderDictionary as json.
        Assert.Contains(_fixture.ServerLogs, log => log.Message.Contains(@"""Authorization"":[""Bearer test-bearer-token-12345""]"));
    }

    public static IEnumerable<object?[]> UseMcpClient_ApprovalsWorkCorrectly_TestData()
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
    [MemberData(nameof(UseMcpClient_ApprovalsWorkCorrectly_TestData))]
    public async Task UseMcpClient_ApprovalsWorkCorrectly(
        bool streaming, 
        HostedMcpServerToolApprovalMode? approvalMode,
        string[] expectedApprovalRequiredAIFunctions,
        string[] expectedNormalAIFunctions)
    {
        // Arrange
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var mcpTool = new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint)
        {
            ApprovalMode = approvalMode
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        await GetResponseAsync(sut, options, streaming);

        // Assert
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        Assert.Equal(3, callbackState.CapturedOptions.Tools.Count);

        var toolsRequiringApproval = callbackState.CapturedOptions.Tools
            .Where(t => t is ApprovalRequiredAIFunction).Select(t => t.Name);

        var toolsNotRequiringApproval = callbackState.CapturedOptions.Tools
            .Where(t => t is not ApprovalRequiredAIFunction).Select(t => t.Name);

        Assert.Equivalent(expectedApprovalRequiredAIFunctions, toolsRequiringApproval);
        Assert.Equivalent(expectedNormalAIFunctions, toolsNotRequiringApproval);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_ThrowsInvalidOperationException_WhenServerAddressIsInvalid(bool streaming)
    {
        // Arrange
        IChatClient sut = CreateTestChatClient(out _);
        var mcpTool = new HostedMcpServerTool("test-server", "test-connector-123");
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GetResponseAsync(sut, options, streaming));
        Assert.Contains("test-connector-123", exception.Message);
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
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var mcpTool = new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint)
        {
            AllowedTools = allowedTools
        };
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act
        await GetResponseAsync(sut, options, streaming);

        // Assert
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var toolNames = callbackState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Equal(expectedTools.Length, toolNames.Count);
        Assert.Equivalent(expectedTools, toolNames);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseMcpClient_CachesClientForSameServerAddress(bool streaming)
    {
        // Arrange
        IChatClient sut = CreateTestChatClient(out var callbackState);
        var mcpTool = new HostedMcpServerTool(_transportOptions.Name!, _transportOptions.Endpoint);
        var options = new ChatOptions { Tools = [mcpTool] };

        // Act - First call
        await GetResponseAsync(sut, options, streaming);

        // Assert - First call should succeed and produce tools
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var firstCallToolCount = callbackState.CapturedOptions.Tools.Count;
        Assert.Equal(3, firstCallToolCount);

        // Act - Second call with same server address (should use cached client)
        await GetResponseAsync(sut, options, streaming);

        // Assert - Second call should also succeed with same tools
        Assert.NotNull(callbackState.CapturedOptions);
        Assert.NotNull(callbackState.CapturedOptions.Tools);
        var secondCallToolCount = callbackState.CapturedOptions.Tools.Count;
        Assert.Equal(3, secondCallToolCount);
        Assert.Equal(firstCallToolCount, secondCallToolCount);

        // Verify the tools are the same
        var toolNames = callbackState.CapturedOptions.Tools.Select(t => t.Name).ToList();
        Assert.Contains("echo", toolNames);
        Assert.Contains("echoSessionId", toolNames);
        Assert.Contains("sampleLLM", toolNames);
    }
}
