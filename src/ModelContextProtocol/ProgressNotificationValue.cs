namespace ModelContextProtocol;

/// <summary>Provides a progress value that can be sent using <see cref="IProgress{ProgressNotificationValue}"/>.</summary>
public record struct ProgressNotificationValue
{
    /// <summary>
    /// Gets or sets the progress thus far. This value typically represents either a percentage (0-100) or
    /// the number of items processed so far (when used with the <see cref="Total"/> property).
    /// </summary>
    /// <remarks>
    /// When reporting progress, this value should increase monotonically as the operation proceeds.
    /// Values are typically between 0 and 100 when representing percentages, or can be any positive number
    /// when representing completed items in combination with the <see cref="Total"/> property.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Example using progress as a percentage
    /// progress.Report(new ProgressNotificationValue { Progress = 75 });
    /// 
    /// // Example using progress as count of items processed
    /// progress.Report(new ProgressNotificationValue { Progress = 3, Total = 10 });
    /// </code>
    /// </example>
    public required float Progress { get; init; }

    /// <summary>Gets or sets the total number of items to process (or total progress required), if known.</summary>
    public float? Total { get; init; }

    /// <summary>Gets or sets an optional message describing the current progress.</summary>
    public string? Message { get; init; }
}
