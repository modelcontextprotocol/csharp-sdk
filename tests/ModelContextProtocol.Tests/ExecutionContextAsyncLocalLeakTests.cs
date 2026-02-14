namespace ModelContextProtocol.Tests;

public class ExecutionContextAsyncLocalLeakTests
{
    // Use a non-trivial count to model repeated captures and avoid relying on a single snapshot edge case.
    private const int ExecutionContextCaptureCount = 128;
    private static readonly AsyncLocal<object?> s_staticAsyncLocal = new();
    private static ExecutionContext[]? s_keptExecutionContexts;

    [Fact]
    public void CapturedExecutionContexts_KeepStaticAsyncLocalValueAlive_UntilContextsAreReleased()
    {
        var weakReference = SetupAsyncLocalAndCaptureContexts(useStaticAsyncLocal: true);

        ForceFullGc();
        GC.KeepAlive(s_keptExecutionContexts);

        Assert.True(weakReference.IsAlive);

        s_keptExecutionContexts = null;
        s_staticAsyncLocal.Value = null;
        ForceFullGc();

        Assert.False(weakReference.IsAlive);
    }

    [Fact]
    public void CapturedExecutionContexts_KeepNonStaticAsyncLocalValueAlive_UntilContextsAreReleased()
    {
        var weakReference = SetupAsyncLocalAndCaptureContexts(useStaticAsyncLocal: false);

        ForceFullGc();
        GC.KeepAlive(s_keptExecutionContexts);

        Assert.True(weakReference.IsAlive);

        s_keptExecutionContexts = null;
        ForceFullGc();

        Assert.False(weakReference.IsAlive);
    }

    private static WeakReference SetupAsyncLocalAndCaptureContexts(bool useStaticAsyncLocal)
    {
        WeakReference? weakReference = null;

        var thread = new Thread(() =>
        {
            var asyncLocal = useStaticAsyncLocal ? s_staticAsyncLocal : new AsyncLocal<object?>();

            object asyncLocalValue = new();
            weakReference = new(asyncLocalValue);

            asyncLocal.Value = asyncLocalValue;
            s_keptExecutionContexts = CaptureExecutionContexts();
            asyncLocal.Value = null;
        });

        thread.Start();
        thread.Join();

        Assert.NotNull(weakReference);
        return weakReference;
    }

    private static ExecutionContext[] CaptureExecutionContexts()
    {
        // Capture multiple contexts to simulate code that snapshots ExecutionContext repeatedly
        // before invoking work.
        var executionContexts = new ExecutionContext[ExecutionContextCaptureCount];

        for (var i = 0; i < ExecutionContextCaptureCount; i++)
        {
            executionContexts[i] = ExecutionContext.Capture()!;
        }

        return executionContexts;
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
