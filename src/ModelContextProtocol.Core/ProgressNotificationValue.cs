namespace ModelContextProtocol;

/// <summary>
/// Provides a progress value that can be sent using <see cref="IProgress{ProgressNotificationValue}"/>.
/// </summary>
public sealed class ProgressNotificationValue
{
    /// <summary>
    /// Gets or initializes the progress thus far.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value typically represents either a percentage (0-100) or the number of items processed so far (when used with the <see cref="Total"/> property).
    /// </para>
    /// <para>
    /// When reporting progress, this value should increase monotonically as the operation proceeds.
    /// Values are typically between 0 and 100 when representing percentages, or can be any positive number
    /// when representing completed items in combination with the <see cref="Total"/> property.
    /// </para>
    /// </remarks>
    public required float Progress { get; init; }

    /// <summary>Gets or initializes the total number of items to process (or total progress required), if known.</summary>
    public float? Total { get; init; }

    /// <summary>Gets or initializes an optional message describing the current progress.</summary>
    public string? Message { get; init; }
}
