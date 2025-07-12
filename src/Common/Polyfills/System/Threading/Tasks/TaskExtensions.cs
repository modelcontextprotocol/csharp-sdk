#if !NET
using ModelContextProtocol;

namespace System.Threading.Tasks;

internal static class TaskExtensions
{
    public static Task WaitAsync(this Task task, CancellationToken cancellationToken)
    {
        return WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        return WaitAsync(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await WaitAsync((Task)task, timeout, cancellationToken).ConfigureAwait(false);
        return task.Result;
    }

    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(task);

        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (!task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var cancellationTask = new TaskCompletionSource<bool>();
            using var _ = cts.Token.Register(tcs => ((TaskCompletionSource<bool>)tcs!).TrySetResult(true), cancellationTask);
            await Task.WhenAny(task, cancellationTask.Task).ConfigureAwait(false);
            
            if (!task.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException();
            }
        }

        await task.ConfigureAwait(false);
    }
}
#endif