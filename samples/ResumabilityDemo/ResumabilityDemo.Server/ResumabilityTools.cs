using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ResumabilityDemo.Server;

/// <summary>
/// Demo tools for testing streamable HTTP resumability, redelivery, and polling mode.
/// </summary>
[McpServerToolType]
public sealed class ResumabilityTools
{
    private readonly TransportRegistry _registry;
    private readonly ILogger<ResumabilityTools> _logger;

    public ResumabilityTools(TransportRegistry registry, ILogger<ResumabilityTools> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Registers the transport for this request so it can be killed later.
    /// </summary>
    private IDisposable? RegisterTransport(RequestContext<CallToolRequestParams> context, string toolName)
    {
        var transport = context.JsonRpcRequest.Context?.RelatedTransport;
        if (transport is null)
        {
            _logger.LogDebug("No related transport available for {ToolName}", toolName);
            return null;
        }

        var id = $"{toolName}-{context.JsonRpcRequest.Id}";
        return _registry.Register(id, transport);
    }

    /// <summary>
    /// A simple echo tool that returns immediately. Use this to verify basic connectivity.
    /// </summary>
    [McpServerTool, Description("Echoes the input message back. Use for basic connectivity testing.")]
    public string Echo([Description("The message to echo")] string message)
    {
        _logger.LogInformation("Echo called with message: {Message}", message);
        return $"Echo: {message}";
    }

    /// <summary>
    /// A slow tool that simulates long-running work. The server will complete the response
    /// after the delay, allowing you to test what happens when the client disconnects
    /// and reconnects mid-operation.
    /// </summary>
    [McpServerTool, Description("Delays for the specified duration before returning. Use to test resumability during long operations.")]
    public async Task<string> DelayedEcho(
        RequestContext<CallToolRequestParams> context,
        [Description("The message to echo")] string message,
        [Description("Delay in seconds before responding (default: 5)")] int delaySeconds = 5,
        CancellationToken cancellationToken = default)
    {
        using var _ = RegisterTransport(context, "DelayedEcho");

        _logger.LogInformation("DelayedEcho starting with {DelaySeconds}s delay", delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

        _logger.LogInformation("DelayedEcho completed after delay");
        return $"Delayed Echo ({delaySeconds}s): {message}";
    }

    /// <summary>
    /// A tool that sends progress notifications at regular intervals. This demonstrates
    /// that multiple SSE events are sent and can be resumed if the client disconnects.
    /// </summary>
    [McpServerTool, Description("Sends progress updates at regular intervals. Use to test resuming mid-stream and receiving missed events.")]
    public async Task<string> ProgressDemo(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Number of progress updates to send (default: 10)")] int steps = 10,
        [Description("Interval between updates in milliseconds (default: 1000)")] int intervalMs = 1000,
        CancellationToken cancellationToken = default)
    {
        using var _ = RegisterTransport(context, "ProgressDemo");

        _logger.LogInformation("ProgressDemo starting with {Steps} steps, {IntervalMs}ms interval", steps, intervalMs);

        var progressToken = context.Params?.ProgressToken;

        for (int i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (progressToken is not null)
            {
                await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
                {
                    Progress = i,
                    Total = steps,
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("ProgressDemo sent progress {Current}/{Total}", i, steps);

            if (i < steps)
            {
                await Task.Delay(intervalMs, cancellationToken);
            }
        }

        _logger.LogInformation("ProgressDemo completed all {Steps} steps", steps);
        return $"Completed {steps} progress steps";
    }

    /// <summary>
    /// A tool that triggers server-side disconnect by calling EnablePollingAsync.
    /// After this, the client must poll for updates instead of maintaining a persistent connection.
    /// This is the key tool for testing the SSE polling resumability feature.
    /// </summary>
    [McpServerTool, Description("Triggers server-side disconnect and switches to polling mode. The client will receive a retry interval and must poll for the response.")]
    public async Task<string> TriggerPollingMode(
        RequestContext<CallToolRequestParams> context,
        [Description("Retry interval in seconds for the client to poll (default: 2)")] int retryIntervalSeconds = 2,
        [Description("Simulated work duration in seconds before returning (default: 5)")] int workDurationSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        using var _ = RegisterTransport(context, "TriggerPollingMode");

        _logger.LogInformation(
            "TriggerPollingMode called. Will disconnect and return after {WorkDuration}s with {RetryInterval}s retry",
            workDurationSeconds, retryIntervalSeconds);

        // Enable polling mode - this ends the current HTTP response and tells the client
        // to poll at the specified interval to get the result
        var retryInterval = TimeSpan.FromSeconds(retryIntervalSeconds);
        await context.EnablePollingAsync(retryInterval, cancellationToken);

        _logger.LogInformation("Polling mode enabled, client should now poll. Doing background work...");

        // Simulate some work that happens after the HTTP response has ended
        // The result will be available when the client polls back
        await Task.Delay(TimeSpan.FromSeconds(workDurationSeconds), cancellationToken);

        _logger.LogInformation("TriggerPollingMode work completed, result ready for polling client");
        return $"Work completed after {workDurationSeconds}s. Client polled successfully!";
    }

    /// <summary>
    /// A tool that combines progress notifications with polling mode transition.
    /// It sends some progress updates, then switches to polling mode, then completes.
    /// </summary>
    [McpServerTool, Description("Sends progress updates, then switches to polling mode mid-stream. Tests the full resumability + polling flow.")]
    public async Task<string> ProgressThenPolling(
        McpServer server,
        RequestContext<CallToolRequestParams> context,
        [Description("Number of progress updates before switching to polling (default: 3)")] int progressSteps = 3,
        [Description("Interval between progress updates in milliseconds (default: 500)")] int progressIntervalMs = 500,
        [Description("Retry interval in seconds for polling (default: 1)")] int retryIntervalSeconds = 1,
        [Description("Work duration after switching to polling in seconds (default: 3)")] int postPollingWorkSeconds = 3,
        CancellationToken cancellationToken = default)
    {
        using var _ = RegisterTransport(context, "ProgressThenPolling");

        _logger.LogInformation(
            "ProgressThenPolling: {Steps} progress steps, then polling with {WorkSeconds}s work",
            progressSteps, postPollingWorkSeconds);

        var progressToken = context.Params?.ProgressToken;

        // Phase 1: Send progress updates while streaming
        for (int i = 1; i <= progressSteps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (progressToken is not null)
            {
                await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
                {
                    Progress = i,
                    Total = progressSteps + 1, // +1 for the polling phase
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("ProgressThenPolling sent streaming progress {Current}/{Total}", i, progressSteps);
            await Task.Delay(progressIntervalMs, cancellationToken);
        }

        // Phase 2: Switch to polling mode
        _logger.LogInformation("ProgressThenPolling switching to polling mode");
        await context.EnablePollingAsync(TimeSpan.FromSeconds(retryIntervalSeconds), cancellationToken);

        // Phase 3: Do work while client is polling
        await Task.Delay(TimeSpan.FromSeconds(postPollingWorkSeconds), cancellationToken);

        // Send final progress (will be delivered when client polls)
        if (progressToken is not null)
        {
            await server.NotifyProgressAsync(progressToken.Value, new ProgressNotificationValue
            {
                Progress = progressSteps + 1,
                Total = progressSteps + 1,
            }, cancellationToken: cancellationToken);
        }

        _logger.LogInformation("ProgressThenPolling completed");
        return $"Completed: {progressSteps} streaming updates + polling work ({postPollingWorkSeconds}s)";
    }

    /// <summary>
    /// A tool that generates a unique ID on each call. Use this to verify that
    /// you're receiving the same response on reconnection (resumability) vs a new response.
    /// </summary>
    [McpServerTool, Description("Generates a unique ID with timestamp. Use to verify resumability returns the same response on reconnection.")]
    public string GenerateUniqueId([Description("Optional prefix for the ID")] string? prefix = null)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var result = string.IsNullOrEmpty(prefix)
            ? $"{id} @ {timestamp}"
            : $"{prefix}-{id} @ {timestamp}";

        _logger.LogInformation("GenerateUniqueId: {Result}", result);
        return result;
    }
}
