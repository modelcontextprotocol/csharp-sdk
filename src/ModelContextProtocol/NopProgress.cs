namespace ModelContextProtocol;

/// <summary>Provides an <see cref="IProgress{ProgressNotificationValue}"/> that's a nop.</summary>
internal sealed class NullProgress : IProgress<ProgressNotificationValue>
{
    public static NullProgress Instance { get; } = new();

    /// <inheritdoc />
    public void Report(ProgressNotificationValue value)
    {
    }
}
