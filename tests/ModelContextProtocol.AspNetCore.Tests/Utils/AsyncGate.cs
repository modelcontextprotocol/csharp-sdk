using ModelContextProtocol.Client;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests.Utils;

internal sealed class AsyncGate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken Token { get; private set; }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Token = cancellationToken;
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

    public async Task WaitUntilEnteredAsync(Task<McpClient> connecting)
    {
        await Task.WhenAny(Entered.Task, connecting).WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        if (!Entered.Task.IsCompleted)
        {
            await using var client = await connecting;
            Assert.Fail("The client connected without entering the expected phase.");
        }
    }
}
