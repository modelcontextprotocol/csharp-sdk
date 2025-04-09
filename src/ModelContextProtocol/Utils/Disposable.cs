namespace ModelContextProtocol.Utils;

/// <summary>
/// A utility class that implements <see cref="IDisposable"/> and executes an action when disposed.
/// </summary>
/// <remarks>
/// Creates a new instance of the <see cref="Disposable"/> class.
/// </remarks>
/// <param name="disposeAction">The action to execute when this object is disposed.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="disposeAction"/> is null.</exception>
public sealed class Disposable(Action disposeAction) : IDisposable
{
    private Action? _disposeAction = disposeAction ?? throw new ArgumentNullException(nameof(disposeAction));
    private bool _isDisposed;

    /// <summary>
    /// Finalizer to ensure resources are properly cleaned up.
    /// </summary>
    ~Disposable() => Dispose(false);

    /// <summary>
    /// Creates an empty disposable that does nothing when disposed.
    /// </summary>
    /// <returns>A disposable object that does nothing when disposed.</returns>
    public static IDisposable Empty() => new Disposable(() => { });

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                // Call the dispose action only in the explicit disposal path
                _disposeAction?.Invoke();
            }

            _disposeAction = null;
            _isDisposed = true;
        }
    }

    /// <summary>
    /// Implicitly converts an <see cref="Action"/> to a <see cref="Disposable"/>.
    /// </summary>
    public static implicit operator Disposable(Action disposable) => new(disposable);
}
