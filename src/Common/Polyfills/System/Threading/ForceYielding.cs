#if !NET
using System.Runtime.CompilerServices;

namespace System.Threading;

/// <summary>
/// await default(ForceYielding) to provide the same behavior as
/// await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding).
/// </summary>
internal readonly struct ForceYielding : INotifyCompletion, ICriticalNotifyCompletion
{
    public ForceYielding GetAwaiter() => this;

    public bool IsCompleted => false;
    public void OnCompleted(Action continuation) => ThreadPool.QueueUserWorkItem(a => ((Action)a!)(), continuation);
    public void UnsafeOnCompleted(Action continuation) => ThreadPool.UnsafeQueueUserWorkItem(a => ((Action)a!)(), continuation);
    public void GetResult() { }
}
#endif