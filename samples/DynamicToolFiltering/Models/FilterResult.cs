namespace DynamicToolFiltering.Models;

/// <summary>
/// Represents the result of a filter operation.
/// </summary>
public class FilterResult
{
    /// <summary>
    /// Gets or sets whether the filter passed.
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Gets or sets the filter name that produced this result.
    /// </summary>
    public string FilterName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the priority of the filter that produced this result.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Gets or sets the reason for the filter result.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets additional data from the filter.
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when the filter was evaluated.
    /// </summary>
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the execution time of the filter in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Creates a successful filter result.
    /// </summary>
    /// <param name="filterName">The name of the filter.</param>
    /// <param name="reason">The reason for success.</param>
    /// <param name="priority">The filter priority.</param>
    /// <returns>A successful filter result.</returns>
    public static FilterResult Success(string filterName, string reason, int priority)
    {
        return new FilterResult
        {
            Passed = true,
            FilterName = filterName,
            Reason = reason,
            Priority = priority
        };
    }

    /// <summary>
    /// Creates a failed filter result.
    /// </summary>
    /// <param name="filterName">The name of the filter.</param>
    /// <param name="reason">The reason for failure.</param>
    /// <param name="priority">The filter priority.</param>
    /// <returns>A failed filter result.</returns>
    public static FilterResult Failure(string filterName, string reason, int priority)
    {
        return new FilterResult
        {
            Passed = false,
            FilterName = filterName,
            Reason = reason,
            Priority = priority
        };
    }
}

/// <summary>
/// Represents a collection of filter results from multiple filters.
/// </summary>
public class FilterResultCollection
{
    /// <summary>
    /// Gets or sets the list of individual filter results.
    /// </summary>
    public List<FilterResult> Results { get; set; } = new();

    /// <summary>
    /// Gets or sets the overall result (all filters must pass).
    /// </summary>
    public bool OverallResult => Results.All(r => r.Passed);

    /// <summary>
    /// Gets or sets the first failed filter result, if any.
    /// </summary>
    public FilterResult? FirstFailure => Results.FirstOrDefault(r => !r.Passed);

    /// <summary>
    /// Gets or sets the total execution time for all filters.
    /// </summary>
    public double TotalExecutionTimeMs => Results.Sum(r => r.ExecutionTimeMs);

    /// <summary>
    /// Gets or sets the number of filters that were evaluated.
    /// </summary>
    public int FilterCount => Results.Count;

    /// <summary>
    /// Adds a filter result to the collection.
    /// </summary>
    /// <param name="result">The filter result to add.</param>
    public void AddResult(FilterResult result)
    {
        Results.Add(result);
    }

    /// <summary>
    /// Gets a summary of the filter results.
    /// </summary>
    /// <returns>A summary string of the filter results.</returns>
    public string GetSummary()
    {
        if (OverallResult)
        {
            return $"All {FilterCount} filters passed in {TotalExecutionTimeMs:F2}ms";
        }
        else
        {
            var firstFailure = FirstFailure!;
            return $"Filter '{firstFailure.FilterName}' failed: {firstFailure.Reason}";
        }
    }
}