namespace DynamicToolFiltering.Models;

/// <summary>
/// Represents usage statistics for a user or tool.
/// </summary>
public class UsageStatistics
{
    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total number of tool executions.
    /// </summary>
    public int TotalExecutions { get; set; }

    /// <summary>
    /// Gets or sets the number of successful executions.
    /// </summary>
    public int SuccessfulExecutions { get; set; }

    /// <summary>
    /// Gets or sets the number of failed executions.
    /// </summary>
    public int FailedExecutions { get; set; }

    /// <summary>
    /// Gets or sets the number of executions blocked by filters.
    /// </summary>
    public int BlockedExecutions { get; set; }

    /// <summary>
    /// Gets or sets per-tool usage counts.
    /// </summary>
    public Dictionary<string, int> ToolUsageCounts { get; set; } = new();

    /// <summary>
    /// Gets or sets per-filter block counts.
    /// </summary>
    public Dictionary<string, int> FilterBlockCounts { get; set; } = new();

    /// <summary>
    /// Gets or sets the first execution timestamp.
    /// </summary>
    public DateTime? FirstExecutionAt { get; set; }

    /// <summary>
    /// Gets or sets the last execution timestamp.
    /// </summary>
    public DateTime? LastExecutionAt { get; set; }

    /// <summary>
    /// Gets or sets the current quota usage.
    /// </summary>
    public int QuotaUsed { get; set; }

    /// <summary>
    /// Gets or sets the quota limit.
    /// </summary>
    public int QuotaLimit { get; set; }

    /// <summary>
    /// Gets or sets when the quota period resets.
    /// </summary>
    public DateTime? QuotaResetAt { get; set; }

    /// <summary>
    /// Gets the success rate as a percentage.
    /// </summary>
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;

    /// <summary>
    /// Gets the block rate as a percentage.
    /// </summary>
    public double BlockRate => TotalExecutions > 0 ? (double)BlockedExecutions / TotalExecutions * 100 : 0;

    /// <summary>
    /// Gets the remaining quota.
    /// </summary>
    public int RemainingQuota => Math.Max(0, QuotaLimit - QuotaUsed);

    /// <summary>
    /// Gets whether the quota is unlimited.
    /// </summary>
    public bool IsUnlimitedQuota => QuotaLimit == -1;
}

/// <summary>
/// Represents aggregated usage statistics across multiple users or time periods.
/// </summary>
public class AggregatedUsageStatistics
{
    /// <summary>
    /// Gets or sets the time period for these statistics.
    /// </summary>
    public TimeSpan Period { get; set; }

    /// <summary>
    /// Gets or sets the start time of the statistics period.
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Gets or sets the end time of the statistics period.
    /// </summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// Gets or sets the total number of unique users.
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// Gets or sets the total number of tool executions.
    /// </summary>
    public int TotalExecutions { get; set; }

    /// <summary>
    /// Gets or sets the total number of successful executions.
    /// </summary>
    public int SuccessfulExecutions { get; set; }

    /// <summary>
    /// Gets or sets the total number of failed executions.
    /// </summary>
    public int FailedExecutions { get; set; }

    /// <summary>
    /// Gets or sets the total number of blocked executions.
    /// </summary>
    public int BlockedExecutions { get; set; }

    /// <summary>
    /// Gets or sets the most popular tools by execution count.
    /// </summary>
    public Dictionary<string, int> PopularTools { get; set; } = new();

    /// <summary>
    /// Gets or sets the most active users by execution count.
    /// </summary>
    public Dictionary<string, int> ActiveUsers { get; set; } = new();

    /// <summary>
    /// Gets or sets filter blocking statistics.
    /// </summary>
    public Dictionary<string, int> FilterBlockStats { get; set; } = new();

    /// <summary>
    /// Gets or sets peak usage hours.
    /// </summary>
    public Dictionary<int, int> HourlyUsage { get; set; } = new();

    /// <summary>
    /// Gets the overall success rate as a percentage.
    /// </summary>
    public double OverallSuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;

    /// <summary>
    /// Gets the overall block rate as a percentage.
    /// </summary>
    public double OverallBlockRate => TotalExecutions > 0 ? (double)BlockedExecutions / TotalExecutions * 100 : 0;

    /// <summary>
    /// Gets the average executions per user.
    /// </summary>
    public double AverageExecutionsPerUser => UniqueUsers > 0 ? (double)TotalExecutions / UniqueUsers : 0;
}