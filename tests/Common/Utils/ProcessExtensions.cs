namespace System.Diagnostics;

public static class ProcessExtensions
{
    public static async Task WaitForExitAsync(this Process process, TimeSpan timeout)
    {
#if NET
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        shutdownCts.CancelAfter(timeout);
        await process.WaitForExitAsync(shutdownCts.Token);
#else
        process.WaitForExit(milliseconds: (int)timeout.TotalMilliseconds);
#endif
    }
}