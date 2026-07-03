using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsRequestFilterTests(ITestOutputHelper testOutputHelper) : ClientServerTestBase(testOutputHelper)
{
    private static ILogger GetLogger(IServiceProvider? services, string categoryName)
    {
        var loggerFactory = services?.GetRequiredService<ILoggerFactory>() ?? throw new InvalidOperationException("LoggerFactory not available");
        return loggerFactory.CreateLogger(categoryName);
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder
            .WithRequestFilters(filters =>
            {
                filters.AddListResourceTemplatesFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListResourceTemplatesFilter");
                    logger.LogInformation("ListResourceTemplatesFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListToolsFilter");
                    logger.LogInformation("ListToolsFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListToolsOrder1");
                    logger.LogInformation("ListToolsOrder1 before");
                    var result = await next(request, cancellationToken);
                    logger.LogInformation("ListToolsOrder1 after");
                    return result;
                });

                filters.AddListToolsFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListToolsOrder2");
                    logger.LogInformation("ListToolsOrder2 before");
                    var result = await next(request, cancellationToken);
                    logger.LogInformation("ListToolsOrder2 after");
                    return result;
                });

                filters.AddCallToolFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "CallToolFilter");
                    var primitiveId = request.MatchedPrimitive?.Id ?? "unknown";
                    logger.LogInformation($"CallToolFilter executed for tool: {primitiveId}");
                    try
                    {
                        return await next(request, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        return new CallToolResult
                        {
                            Content = [new TextContentBlock { Text = $"Error from filter: {ex.Message}" }],
                            IsError = true
                        };
                    }
                });

                filters.AddListPromptsFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListPromptsFilter");
                    logger.LogInformation("ListPromptsFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddGetPromptFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "GetPromptFilter");
                    var primitiveId = request.MatchedPrimitive?.Id ?? "unknown";
                    logger.LogInformation($"GetPromptFilter executed for prompt: {primitiveId}");
                    return await next(request, cancellationToken);
                });

                filters.AddListResourcesFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ListResourcesFilter");
                    logger.LogInformation("ListResourcesFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddReadResourceFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "ReadResourceFilter");
                    var primitiveId = request.MatchedPrimitive?.Id ?? "unknown";
                    logger.LogInformation($"ReadResourceFilter executed for resource: {primitiveId}");
                    return await next(request, cancellationToken);
                });

                filters.AddCompleteFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "CompleteFilter");
                    logger.LogInformation("CompleteFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddSubscribeToResourcesFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "SubscribeToResourcesFilter");
                    logger.LogInformation("SubscribeToResourcesFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddUnsubscribeFromResourcesFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "UnsubscribeFromResourcesFilter");
                    logger.LogInformation("UnsubscribeFromResourcesFilter executed");
                    return await next(request, cancellationToken);
                });

                filters.AddSetLoggingLevelFilter((next) => async (request, cancellationToken) =>
                {
                    var logger = GetLogger(request.Services, "SetLoggingLevelFilter");
                    logger.LogInformation("SetLoggingLevelFilter executed");
                    return await next(request, cancellationToken);
                });
            })
            .WithTools<TestTool>()
            .WithPrompts<TestPrompt>()
            .WithResources<TestResource>()
            .WithSetLoggingLevelHandler(async (request, cancellationToken) => new EmptyResult())
            .WithListResourceTemplatesHandler(async (request, cancellationToken) => new ListResourceTemplatesResult
            {
                ResourceTemplates = [new() { Name = "test", UriTemplate = "test://resource/{id}" }]
            })
            .WithCompleteHandler(async (request, cancellationToken) => new CompleteResult
            {
                Completion = new() { Values = ["test"] }
            });
    }

    [Fact]
    public async Task AddListResourceTemplatesFilter_Logs_When_ListResourceTemplates_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ListResourceTemplatesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ListResourceTemplatesFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("ListResourceTemplatesFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddListToolsFilter_Logs_When_ListTools_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ListToolsFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("ListToolsFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddCallToolFilter_Logs_When_CallTool_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.CallToolAsync("test_tool_method", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "CallToolFilter executed for tool: test_tool_method");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("CallToolFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddCallToolFilter_Catches_Exception_From_Tool()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var result = await client.CallToolAsync("throwing_tool_method", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.NotNull(result.Content);
        var textContent = Assert.Single(result.Content);
        var textBlock = Assert.IsType<TextContentBlock>(textContent);
        Assert.Equal("Error from filter: This tool always throws an exception", textBlock.Text);
    }

    [Fact]
    public async Task AddListPromptsFilter_Logs_When_ListPrompts_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ListPromptsFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("ListPromptsFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddGetPromptFilter_Logs_When_GetPrompt_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.GetPromptAsync("test_prompt_method", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "GetPromptFilter executed for prompt: test_prompt_method");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("GetPromptFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddListResourcesFilter_Logs_When_ListResources_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ListResourcesAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ListResourcesFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("ListResourcesFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddReadResourceFilter_Logs_When_ReadResource_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ReadResourceAsync("test://resource/123", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "ReadResourceFilter executed for resource: test://resource/{id}");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("ReadResourceFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddCompleteFilter_Logs_When_Complete_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var reference = new PromptReference { Name = "test_prompt_method" };
        await client.CompleteAsync(reference, "argument", "value", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "CompleteFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("CompleteFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddSubscribeToResourcesFilter_Logs_When_SubscribeToResources_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.SubscribeToResourceAsync("test://resource/123", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "SubscribeToResourcesFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("SubscribeToResourcesFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddUnsubscribeFromResourcesFilter_Logs_When_UnsubscribeFromResources_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.UnsubscribeFromResourceAsync("test://resource/123", cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "UnsubscribeFromResourcesFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("UnsubscribeFromResourcesFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddSetLoggingLevelFilter_Logs_When_SetLoggingLevel_Called()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.SetLoggingLevelAsync(LoggingLevel.Info, cancellationToken: TestContext.Current.CancellationToken);

        var logMessage = Assert.Single(MockLoggerProvider.LogMessages, m => m.Message == "SetLoggingLevelFilter executed");
        Assert.Equal(LogLevel.Information, logMessage.LogLevel);
        Assert.Equal("SetLoggingLevelFilter", logMessage.Category);
    }

    [Fact]
    public async Task AddListToolsFilter_Multiple_Filters_Log_In_Expected_Order()
    {
        await using McpClient client = await CreateMcpClientForServer();

        await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var logMessages = MockLoggerProvider.LogMessages
            .Where(m => m.Category.StartsWith("ListToolsOrder"))
            .Select(m => m.Message);

        Assert.Collection(logMessages,
            m => Assert.Equal("ListToolsOrder1 before", m),
            m => Assert.Equal("ListToolsOrder2 before", m),
            m => Assert.Equal("ListToolsOrder2 after", m),
            m => Assert.Equal("ListToolsOrder1 after", m)
        );
    }

    [McpServerToolType]
    public sealed class TestTool
    {
        [McpServerTool]
        public static string TestToolMethod()
        {
            return "test result";
        }

        [McpServerTool]
        public static string ThrowingToolMethod()
        {
            throw new InvalidOperationException("This tool always throws an exception");
        }
    }

    [McpServerPromptType]
    public sealed class TestPrompt
    {
        [McpServerPrompt]
        public static Task<GetPromptResult> TestPromptMethod()
        {
            return Task.FromResult(new GetPromptResult
            {
                Description = "Test prompt",
                Messages = [new() { Role = Role.User, Content = new TextContentBlock { Text = "Test" } }]
            });
        }
    }

    [McpServerResourceType]
    public sealed class TestResource
    {
        [McpServerResource(UriTemplate = "test://resource/{id}")]
        public static string TestResourceMethod(string id)
        {
            return $"Test resource for ID: {id}";
        }
    }
}
