using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

internal sealed class AsyncGate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        try
        {
            await Release.Task.WaitAsync(TestConstants.DefaultTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Canceled.TrySetResult();
            throw;
        }
    }

    public async Task AssertStillWaitingAsync(TimeSpan duration)
    {
        await Entered.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => Canceled.Task.WaitAsync(duration, TestContext.Current.CancellationToken));
    }
}
