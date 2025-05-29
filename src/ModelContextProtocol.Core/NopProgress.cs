namespace ModelContextProtocol;

/// <summary>Provides an <see cref="IProgress{ProgressNotificationValue}"/> that's a nop.</summary>
internal sealed class NullProgress : IProgress<ProgressNotificationValue>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="NullProgress"/> class that performs no operations when progress is reported.
    /// </summary>
    /// <remarks>
    /// Use this property when you need to provide an <see cref="IProgress{T}"/> implementation 
    /// but don't need to track or report actual progress.
    /// </remarks>
    public static NullProgress Instance { get; } = new();

    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
    }
}
