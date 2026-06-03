using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Regression tests guarding the <see cref="TimeSpanMillisecondsConverter"/> behavior that is
/// <em>shared</em> between SEP-2549 caching hints and <see cref="McpTask"/>'s <c>ttl</c>/<c>pollInterval</c>.
/// The converter's read path was hardened to clamp out-of-range values instead of throwing; these tests
/// ensure that change did not alter behavior for normal McpTask values and that the clamping also
/// protects McpTask deserialization from hostile/oversized inputs.
/// </summary>
public static class TimeSpanMillisecondsConverterSharedTests
{
    private const string TaskEnvelope =
        "{{\"taskId\":\"t1\",\"status\":\"working\",\"createdAt\":\"2024-01-01T00:00:00Z\"," +
        "\"lastUpdatedAt\":\"2024-01-01T00:00:00Z\",{0}}}";

    [Fact]
    public static void McpTask_NormalTtlAndPollInterval_RoundTripUnchanged()
    {
        string json = string.Format(TaskEnvelope, "\"ttl\":86400000,\"pollInterval\":1000");

        var task = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions)!;

        Assert.Equal(TimeSpan.FromDays(1), task.TimeToLive);
        Assert.Equal(TimeSpan.FromSeconds(1), task.PollInterval);

        // Re-serialize and confirm the integer millisecond values are preserved exactly.
        string reserialized = JsonSerializer.Serialize(task, McpJsonUtilities.DefaultOptions);
        var rt = JsonSerializer.Deserialize<McpTask>(reserialized, McpJsonUtilities.DefaultOptions)!;
        Assert.Equal(TimeSpan.FromDays(1), rt.TimeToLive);
        Assert.Equal(TimeSpan.FromSeconds(1), rt.PollInterval);
    }

    [Fact]
    public static void McpTask_OversizedTtl_ClampsInsteadOfThrowing()
    {
        // The same hardening that protects cacheable results must also keep McpTask deserialization
        // from throwing on an out-of-range ttl.
        string json = string.Format(TaskEnvelope, "\"ttl\":9999999999999999");

        var task = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions)!;

        Assert.Equal(TimeSpan.MaxValue, task.TimeToLive);
    }

    [Fact]
    public static void McpTask_NegativeInfinityPollInterval_ClampsToMinValue()
    {
        string json = string.Format(TaskEnvelope, "\"pollInterval\":-1e400");

        var task = JsonSerializer.Deserialize<McpTask>(json, McpJsonUtilities.DefaultOptions)!;

        Assert.Equal(TimeSpan.MinValue, task.PollInterval);
    }
}
