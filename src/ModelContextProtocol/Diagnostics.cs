using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ModelContextProtocol;

internal static class Diagnostics
{
    internal static ActivitySource ActivitySource { get; } = new("Experimental.ModelContextProtocol");

    internal static Meter Meter { get; } = new("Experimental.ModelContextProtocol");

    internal static Histogram<double> CreateDurationHistogram(string name, string description, bool longBuckets) =>
        Diagnostics.Meter.CreateHistogram<double>(name, "s", description
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
}
