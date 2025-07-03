using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol;

internal static class Diagnostics
{
    internal static ActivitySource ActivitySource { get; } = new("Experimental.ModelContextProtocol");

    internal static Meter Meter { get; } = new("Experimental.ModelContextProtocol");

    internal static Histogram<double> CreateDurationHistogram(string name, string description, bool longBuckets) =>
        Meter.CreateHistogram<double>(name, "s", description
#if NET9_0_OR_GREATER
        , advice: longBuckets ? LongSecondsBucketBoundaries : ShortSecondsBucketBoundaries
#endif
        );

#if NET9_0_OR_GREATER
    /// <summary>
    /// Follows boundaries from http.server.request.duration/http.client.request.duration
    /// </summary>
    private static InstrumentAdvice<double> ShortSecondsBucketBoundaries { get; } = new()
    {
        HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10],
    };

    /// <summary>
    /// Not based on a standard. Larger bucket sizes for longer lasting operations, e.g. HTTP connection duration.
    /// See https://github.com/open-telemetry/semantic-conventions/issues/336
    /// </summary>
    private static InstrumentAdvice<double> LongSecondsBucketBoundaries { get; } = new()
    {
        HistogramBucketBoundaries = [0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 30, 60, 120, 300],
    };
#endif

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
                meta = new JsonObject();
                jsonObject["_meta"] = meta;
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

    internal static ActivityLink[] ActivityLinkFromCurrent() => Activity.Current is null ? [] : [new ActivityLink(Activity.Current.Context)];
}
