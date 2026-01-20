using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

internal static class Diagnostics
{
    internal static ActivitySource ActivitySource { get; } = new("Experimental.ModelContextProtocol");

    internal static Meter Meter { get; } = new("Experimental.ModelContextProtocol");

    internal static Histogram<double> CreateDurationHistogram(string name, string description) =>
        Meter.CreateHistogram(name, "s", description, advice: ExplicitBucketBoundaries);

    /// <summary>
    /// ExplicitBucketBoundaries specified in MCP semantic conventions for all MCP metrics.
    /// See https://github.com/open-telemetry/semantic-conventions/blob/main/docs/gen-ai/mcp.md#metrics
    /// </summary>
    private static InstrumentAdvice<double> ExplicitBucketBoundaries { get; } = new()
    {
        HistogramBucketBoundaries = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30, 60, 120, 300],
    };

    internal static ActivityContext ExtractActivityContext(this DistributedContextPropagator propagator, JsonRpcMessage message)
    {
        propagator.ExtractTraceIdAndState(message, ExtractContext, out var traceparent, out var tracestate);
        ActivityContext.TryParse(traceparent, tracestate, true, out var activityContext);
        return activityContext;
    }

    private static void ExtractContext(object? message, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues)
    {
        fieldValues = null;
        fieldValue = null;

        JsonNode? meta = null;
        switch (message)
        {
            case JsonRpcRequest request:
                meta = request.Params?["_meta"];
                break;

            case JsonRpcNotification notification:
                meta = notification.Params?["_meta"];
                break;
        }

        if (meta?[fieldName] is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            fieldValue = value.GetValue<string>();
        }
    }

    internal static void InjectActivityContext(this DistributedContextPropagator propagator, Activity? activity, JsonRpcMessage message)
    {
        // noop if activity is null
        propagator.Inject(activity, message, InjectContext);
    }

    private static void InjectContext(object? message, string key, string value)
    {
        JsonNode? parameters = null;
        switch (message)
        {
            case JsonRpcRequest request:
                parameters = request.Params;
                break;

            case JsonRpcNotification notification:
                parameters = notification.Params;
                break;
        }

        // Replace any params._meta with the current value
        if (parameters is JsonObject jsonObject)
        {
            if (jsonObject["_meta"] is not JsonObject meta)
            {
                jsonObject["_meta"] = meta = [];
            }

            meta[key] = value;
        }
    }

    internal static bool ShouldInstrumentMessage(JsonRpcMessage message) =>
        ActivitySource.HasListeners() &&
        message switch
        {
            JsonRpcRequest => true,
            JsonRpcNotification notification => notification.Method != NotificationMethods.LoggingMessageNotification,
            _ => false
        };

    /// <summary>
    /// If outer GenAI instrumentation is already tracing the tool execution,
    /// MCP instrumentation SHOULD add MCP-specific attributes to the existing tool execution span instead
    /// of creating a new one.
    /// </summary>
    /// <param name="activity">The outer activity for tool execution, if found.</param>
    /// <returns>true if an outer tool execution activity was found and can be reused; false otherwise.</returns>
    internal static bool TryGetOuterToolExecutionActivity([NotNullWhen(true)] out Activity? activity)
    {
        if (Activity.Current is { } currentActivity &&
            currentActivity.OperationName.StartsWith("execute_tool ", StringComparison.Ordinal))
        {
            activity = currentActivity;
            return true;
        }

        activity = null;
        return false;
    }

    internal static ActivityLink[] ActivityLinkFromCurrent() => Activity.Current is null ? [] : [new ActivityLink(Activity.Current.Context)];
}
