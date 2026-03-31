#if NET
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Module initializer that hooks global exception events to diagnose async void crashes
/// and other unobserved exceptions during test execution. This is intended to capture
/// evidence for intermittent xUnit test runner hangs where an async void method's exception
/// may go unobserved, preventing a TaskCompletionSource from being signaled.
/// </summary>
internal static class DiagnosticExceptionTracing
{
    private static int s_initialized;
    private static int s_unhandledExceptionCount;
    private static int s_unobservedTaskExceptionCount;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref s_initialized, 1) != 0)
        {
            return;
        }

        // This fires when an async void method throws and the exception propagates to the
        // thread pool. If the xUnit runTest async void function throws before its try block,
        // we'll see it here. Note: this typically terminates the process, so seeing output
        // from this handler is strong evidence of the suspected bug.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            int count = Interlocked.Increment(ref s_unhandledExceptionCount);
            string terminating = e.IsTerminating ? "TERMINATING" : "non-terminating";
            Console.Error.WriteLine($"[DiagnosticExceptionTracing] UnhandledException #{count} ({terminating}): {e.ExceptionObject}");
            Console.Error.Flush();

            // Also write to Trace in case stderr is not captured
            Trace.WriteLine($"[DiagnosticExceptionTracing] UnhandledException #{count} ({terminating}): {e.ExceptionObject}");
        };

        // This fires when a Task's exception is never observed (no await, no Wait, no Result).
        // Could indicate fire-and-forget tasks with silent failures.
        TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            int count = Interlocked.Increment(ref s_unobservedTaskExceptionCount);
            Console.Error.WriteLine($"[DiagnosticExceptionTracing] UnobservedTaskException #{count}: {e.Exception}");
            Console.Error.Flush();
        };

        Console.Error.WriteLine("[DiagnosticExceptionTracing] Exception tracing hooks installed");
        Console.Error.Flush();
    }
}
#endif
