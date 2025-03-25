namespace ModelContextProtocol.Utils;

internal static class SynchronizationExtensions
{
    public static async ValueTask<Releaser> LockAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken = default)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new(semaphore);
    }

    public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
