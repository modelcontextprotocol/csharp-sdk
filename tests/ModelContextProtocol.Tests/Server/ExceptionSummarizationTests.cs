using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.ExceptionSummarization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

/// <summary>
/// Covers <see cref="McpServerOptions.ExceptionSummarizer"/>: the opt-in hook that logs a sanitized
/// description instead of the raw <see cref="Exception"/> on server-side failure paths.
/// </summary>
public class ExceptionSummarizationTests : LoggedTest
{
    private const string HandlerFailureMessage = "handler blew up with sensitive details";
    private const string Summary = "sanitized-summary";

    public ExceptionSummarizationTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    private static McpServerOptions CreateOptions() => new()
    {
        ProtocolVersion = "2024-11-05",
        InitializationTimeout = TimeSpan.FromSeconds(30),
        Capabilities = new ServerCapabilities { Tools = new() },
    };

    /// <summary>Records how many times it was invoked so tests can assert it never ran.</summary>
    private sealed class CountingSummarizer
    {
        private int _invocations;

        public int Invocations => Volatile.Read(ref _invocations);

        public string Summarize(Exception exception)
        {
            Interlocked.Increment(ref _invocations);
            return Summary;
        }
    }

    #region McpSessionHandler.LogRequestHandlerException

    [Fact]
    public async Task RequestHandlerFailure_WithoutSummarizer_LogsRawException()
    {
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);

        var logs = await RunFailingListToolsRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.Message.Contains("request handler failed"));
        Assert.Equal(LogLevel.Warning, log.LogLevel);
        var exception = Assert.IsType<InvalidOperationException>(log.Exception);
        Assert.Equal(HandlerFailureMessage, exception.Message);
        Assert.DoesNotContain(Summary, log.Message);
    }

    [Fact]
    public async Task RequestHandlerFailure_WithSummarizer_LogsSummaryAndNoException()
    {
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.ExceptionSummarizer = ex => Summary;

        var logs = await RunFailingListToolsRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.Message.Contains("request handler failed"));
        Assert.Equal(LogLevel.Warning, log.LogLevel);
        Assert.Contains(Summary, log.Message);
        Assert.DoesNotContain(HandlerFailureMessage, log.Message);
        Assert.Null(log.Exception);
    }

    [Fact]
    public async Task RequestHandlerFailure_WithThrowingSummarizer_FallsBackToRawException()
    {
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.ExceptionSummarizer = ex => throw new NotSupportedException("summarizer is broken");

        var logs = await RunFailingListToolsRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.Message.Contains("request handler failed"));
        Assert.Equal(LogLevel.Warning, log.LogLevel);
        var exception = Assert.IsType<InvalidOperationException>(log.Exception);
        Assert.Equal(HandlerFailureMessage, exception.Message);
        Assert.DoesNotContain("summarizer is broken", log.Message);
    }

    [Fact]
    public async Task RequestHandlerFailure_WithNullReturningSummarizer_FallsBackToRawException()
    {
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.ExceptionSummarizer = ex => null!;

        var logs = await RunFailingListToolsRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.Message.Contains("request handler failed"));
        Assert.IsType<InvalidOperationException>(log.Exception);
    }

    #endregion

    #region McpServerImpl.ToolCallError

    [Fact]
    public async Task ToolCallFailure_WithoutSummarizer_LogsRawException()
    {
        var options = CreateOptions();
        options.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();

        var logs = await RunFailingCallToolRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal("\"\" threw an unhandled exception.", log.Message);
        var exception = Assert.IsType<InvalidOperationException>(log.Exception);
        Assert.Equal(HandlerFailureMessage, exception.Message);
    }

    [Fact]
    public async Task ToolCallFailure_WithSummarizer_LogsSummaryAndNoException()
    {
        var options = CreateOptions();
        options.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        options.ExceptionSummarizer = ex => Summary;

        var logs = await RunFailingCallToolRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal($"\"\" threw an unhandled exception: {Summary}.", log.Message);
        Assert.Null(log.Exception);
    }

    [Fact]
    public async Task ToolCallFailure_WithThrowingSummarizer_FallsBackToRawException()
    {
        var options = CreateOptions();
        options.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        options.ExceptionSummarizer = ex => throw new NotSupportedException("summarizer is broken");

        var logs = await RunFailingCallToolRequestAsync(options);

        var log = Assert.Single(logs.LogMessages, m => m.LogLevel == LogLevel.Error);
        Assert.Equal("\"\" threw an unhandled exception.", log.Message);
        Assert.IsType<InvalidOperationException>(log.Exception);
    }

    #endregion

    #region The summarizer must not run when the event's level is disabled

    [Fact]
    public async Task RequestHandlerFailure_WhenWarningDisabled_DoesNotInvokeSummarizer()
    {
        CountingSummarizer summarizer = new();
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.ExceptionSummarizer = summarizer.Summarize;

        // LogRequestHandlerException is Warning; a Critical minimum filters it out, so nothing is emitted.
        var logs = await RunFailingListToolsRequestAsync(options, LogLevel.Critical);

        Assert.Empty(logs.LogMessages);
        Assert.Equal(0, summarizer.Invocations);
    }

    [Fact]
    public async Task RequestHandlerFailure_WhenWarningEnabled_InvokesSummarizer()
    {
        CountingSummarizer summarizer = new();
        var options = CreateOptions();
        options.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.ExceptionSummarizer = summarizer.Summarize;

        var logs = await RunFailingListToolsRequestAsync(options, LogLevel.Warning);

        Assert.Single(logs.LogMessages, m => m.Message.Contains("request handler failed") && m.Message.Contains(Summary));
        Assert.Equal(1, summarizer.Invocations);
    }

    [Fact]
    public async Task ToolCallFailure_WhenErrorDisabled_DoesNotInvokeSummarizer()
    {
        CountingSummarizer summarizer = new();
        var options = CreateOptions();
        options.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        options.ExceptionSummarizer = summarizer.Summarize;

        // ToolCallError is Error; a Critical minimum filters it out, so nothing is emitted.
        var logs = await RunFailingCallToolRequestAsync(options, LogLevel.Critical);

        Assert.Empty(logs.LogMessages);
        Assert.Equal(0, summarizer.Invocations);
    }

    [Fact]
    public async Task ToolCallFailure_WhenErrorEnabled_InvokesSummarizer()
    {
        CountingSummarizer summarizer = new();
        var options = CreateOptions();
        options.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        options.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        options.ExceptionSummarizer = summarizer.Summarize;

        var logs = await RunFailingCallToolRequestAsync(options, LogLevel.Error);

        Assert.Single(logs.LogMessages, m => m.Message == $"\"\" threw an unhandled exception: {Summary}.");
        Assert.Equal(1, summarizer.Invocations);
    }

    #endregion

    #region EventId identity is preserved across raw and summarized variants

    [Fact]
    public async Task RequestHandlerFailure_EventIdIsIdenticalWithAndWithoutSummarizer()
    {
        var rawOptions = CreateOptions();
        rawOptions.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        var rawLogs = await RunFailingListToolsRequestAsync(rawOptions);
        var raw = Assert.Single(rawLogs.LogMessages, m => m.Message.Contains("request handler failed"));

        var summarizedOptions = CreateOptions();
        summarizedOptions.Handlers.ListToolsHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        summarizedOptions.ExceptionSummarizer = ex => Summary;
        var summarizedLogs = await RunFailingListToolsRequestAsync(summarizedOptions);
        var summarized = Assert.Single(summarizedLogs.LogMessages, m => m.Message.Contains("request handler failed"));

        // Different rendering, same logical event, so EventId-based filtering keeps working.
        Assert.NotEqual(raw.Message, summarized.Message);
        Assert.Equal(raw.EventId, summarized.EventId);
        Assert.Equal(raw.EventId.Id, summarized.EventId.Id);
        Assert.Equal(raw.EventId.Name, summarized.EventId.Name);
    }

    [Fact]
    public async Task ToolCallFailure_EventIdIsIdenticalWithAndWithoutSummarizer()
    {
        var rawOptions = CreateOptions();
        rawOptions.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        rawOptions.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        var rawLogs = await RunFailingCallToolRequestAsync(rawOptions);
        var raw = Assert.Single(rawLogs.LogMessages, m => m.LogLevel == LogLevel.Error);

        var summarizedOptions = CreateOptions();
        summarizedOptions.Handlers.CallToolHandler = (request, ct) => throw new InvalidOperationException(HandlerFailureMessage);
        summarizedOptions.Handlers.ListToolsHandler = (request, ct) => throw new NotImplementedException();
        summarizedOptions.ExceptionSummarizer = ex => Summary;
        var summarizedLogs = await RunFailingCallToolRequestAsync(summarizedOptions);
        var summarized = Assert.Single(summarizedLogs.LogMessages, m => m.LogLevel == LogLevel.Error);

        Assert.NotEqual(raw.Message, summarized.Message);
        Assert.Equal(raw.EventId, summarized.EventId);
        Assert.Equal(raw.EventId.Id, summarized.EventId.Id);
        Assert.Equal(raw.EventId.Name, summarized.EventId.Name);
    }

    #endregion

    #region Dependency injection wiring

    [Fact]
    public async Task OptionsSetup_WithoutRegisteredSummarizer_LeavesDelegateNull()
    {
        ServiceCollection services = new();
        services.AddMcpServer();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Null(options.ExceptionSummarizer);
    }

    [Fact]
    public async Task OptionsSetup_WithRegisteredSummarizer_PrependsExceptionTypeToDescription()
    {
        ServiceCollection services = new();
        services.AddSingleton<IExceptionSummarizer>(new FakeExceptionSummarizer(Summary));
        services.AddMcpServer();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.ExceptionSummarizer);
        Assert.Equal(
            $"InvalidOperationException: {Summary}",
            options.ExceptionSummarizer(new InvalidOperationException(HandlerFailureMessage)));
    }

    [Fact]
    public async Task OptionsSetup_WithAddExceptionSummarizer_PreservesExceptionTypeAndOmitsMessage()
    {
        ServiceCollection services = new();
        services.AddExceptionSummarizer();
        services.AddMcpServer();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.ExceptionSummarizer);

        // The built-in summarizer has no provider for InvalidOperationException, so Description alone is
        // "Unknown". The exception type must still survive, and the sensitive message must not appear.
        string summary = options.ExceptionSummarizer(new InvalidOperationException(HandlerFailureMessage));
        Assert.StartsWith("InvalidOperationException:", summary);
        Assert.DoesNotContain(HandlerFailureMessage, summary);
    }

    [Fact]
    public async Task OptionsSetup_WithExplicitlyConfiguredDelegate_DoesNotOverwriteIt()
    {
        ServiceCollection services = new();
        services.AddSingleton<IExceptionSummarizer>(new FakeExceptionSummarizer(Summary));
        services.AddMcpServer(options => options.ExceptionSummarizer = ex => "explicit");

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.NotNull(options.ExceptionSummarizer);
        Assert.Equal("explicit", options.ExceptionSummarizer(new InvalidOperationException()));
    }

    private sealed class FakeExceptionSummarizer(string description) : IExceptionSummarizer
    {
        public ExceptionSummary Summarize(Exception exception) =>
            new(exception.GetType().Name, description, string.Empty);
    }

    #endregion

    private async Task<MockLoggerProvider> RunFailingListToolsRequestAsync(
        McpServerOptions options, LogLevel minimumLevel = LogLevel.Debug)
    {
        MockLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(logs);
            builder.SetMinimumLevel(minimumLevel);
        });

        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, loggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var receivedError = new TaskCompletionSource<JsonRpcError>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcError error)
            {
                receivedError.TrySetResult(error);
            }
        };

        await transport.SendMessageAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsList, Id = new RequestId(1) },
            TestContext.Current.CancellationToken);

        await receivedError.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        await transport.DisposeAsync();
        await runTask;

        return logs;
    }

    private async Task<MockLoggerProvider> RunFailingCallToolRequestAsync(
        McpServerOptions options, LogLevel minimumLevel = LogLevel.Debug)
    {
        MockLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(logs);
            builder.SetMinimumLevel(minimumLevel);
        });

        await using var transport = new TestServerTransport();
        await using var server = McpServer.Create(transport, options, loggerFactory);
        var runTask = server.RunAsync(TestContext.Current.CancellationToken);

        var receivedResponse = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnMessageSent = message =>
        {
            if (message is JsonRpcResponse response)
            {
                receivedResponse.TrySetResult(response);
            }
        };

        await transport.SendMessageAsync(
            new JsonRpcRequest { Method = RequestMethods.ToolsCall, Id = new RequestId(1) },
            TestContext.Current.CancellationToken);

        await receivedResponse.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);

        await transport.DisposeAsync();
        await runTask;

        return logs;
    }
}
