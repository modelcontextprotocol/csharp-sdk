namespace ModelContextProtocol.Tests;

public class ExecutionContextAsyncLocalLeakTests
{
    private static readonly AsyncLocal<object?> s_staticAsyncLocal = new();
    private static ExecutionContext[]? s_keptExecutionContexts;

    [Fact]
    public void CapturedExecutionContexts_KeepStaticAsyncLocalValueAlive_UntilContextsAreReleased()
    {
        var weakReference = CreateWeakReferenceAndKeepCapturedExecutionContexts(useStaticAsyncLocal: true);

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
        var weakReference = CreateWeakReferenceAndKeepCapturedExecutionContexts(useStaticAsyncLocal: false);

        ForceFullGc();
        GC.KeepAlive(s_keptExecutionContexts);

        Assert.True(weakReference.IsAlive);

        s_keptExecutionContexts = null;
        ForceFullGc();

        Assert.False(weakReference.IsAlive);
    }

    private static WeakReference CreateWeakReferenceAndKeepCapturedExecutionContexts(bool useStaticAsyncLocal)
    {
        WeakReference? weakReference = null;

        var thread = new Thread(() =>
        {
            var asyncLocal = useStaticAsyncLocal ? s_staticAsyncLocal : new AsyncLocal<object?>();

            object value = new();
            weakReference = new(value);

            asyncLocal.Value = value;
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
        const int contextCount = 128;
        var executionContexts = new ExecutionContext[contextCount];

        for (var i = 0; i < contextCount; i++)
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
