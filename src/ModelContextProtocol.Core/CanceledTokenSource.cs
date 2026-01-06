using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Core;

/// <summary>
/// A <see cref="CancellationTokenSource"/> that is already canceled.
/// Disposal is a no-op.
/// </summary>
public sealed class CanceledTokenSource : CancellationTokenSource
{
    /// <summary>
    /// Gets a singleton instance of a canceled token source.
    /// </summary>
    public static readonly CanceledTokenSource Instance = new();

    private CanceledTokenSource()
        => Cancel();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        // No-op
    }

    /// <summary>
    /// Defuses the given <see cref="CancellationTokenSource"/> by optionally canceling it
    /// and replacing it with the singleton canceled instance.
    /// The original token source is left for garbage collection and finalization provided
    /// there are no other references to it outstanding if <paramref name="dispose"/> is false.
    /// </summary>
    /// <param name="cts"> The token source to pseudo-dispose. May be null.</param>
    /// <param name="cancel"> Whether to cancel the token source before pseudo-disposing it.</param>
    /// <param name="dispose"> Whether to call Dispose on the token source.</param>
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods")]
    public static void Defuse(ref CancellationTokenSource cts, bool cancel = true, bool dispose = false)
    {
        // don't null check; allow replacing null, allow throw on attempt to call Cancel
        var orig = cts;
        if (cancel) orig.Cancel();
        Interlocked.Exchange(ref cts, Instance);
        // presume the GC will finalize and dispose the original CTS as needed
        if (dispose) orig.Dispose();
    }
}