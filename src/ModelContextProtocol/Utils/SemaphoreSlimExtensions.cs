namespace ModelContextProtocol.Utils;

internal static class SynchronizationExtensions
{
    /// <summary>
    /// Asynchronously acquires a lock on the semaphore and returns a disposable object that releases the lock when disposed.
    /// </summary>
    /// <param name="semaphore">The semaphore to acquire a lock on.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the semaphore.</param>
    /// <returns>A disposable <see cref="Releaser"/> that releases the semaphore when disposed.</returns>
    /// <remarks>
    /// <para>
    /// This extension method provides a convenient pattern for using a semaphore in asynchronous code,
    /// similar to how the `lock` statement is used in synchronous code.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// using var _ = await _semaphore.LockAsync(cancellationToken).ConfigureAwait(false);
    /// // Code here runs with the semaphore acquired
    /// // The semaphore is automatically released when the using block ends
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> was canceled.</exception>
    public static async ValueTask<Releaser> LockAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(semaphore);
    }

    /// <summary>
    /// A disposable struct that releases a semaphore when disposed.
    /// </summary>
    /// <remarks>
    /// This struct is used with the <see cref="LockAsync"/> extension method to provide
    /// a using-pattern for semaphore locking, similar to lock statements.
    /// </remarks>
    public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        /// <summary>
        /// Releases the semaphore.
        /// </summary>
        /// <remarks>
        /// This method is called automatically when the <see cref="Releaser"/> goes out of scope
        /// in a using statement or expression.
        /// </remarks>
        public void Dispose() => semaphore.Release();
    }
}
