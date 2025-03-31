namespace ModelContextProtocol;

/// <summary>Provides a progress value that can be sent using <see cref="IProgress{ProgressNotificationValue}"/>.</summary>
public record struct ProgressNotificationValue
{
    /// <summary>Gets or sets the progress thus far.</summary>
    public required float Progress { get; init; }

    /// <summary>Gets or sets the total number of items to process (or total progress required), if known.</summary>
    public float? Total { get; init; }

    /// <summary>Gets or sets an optional message describing the current progress.</summary>
    public string? Message { get; init; }
}
