#if !NET
using ModelContextProtocol;

namespace System.Threading.Tasks;

internal static class CancellationTokenSourceExtensions
{
    public static Task CancelAsync(this CancellationTokenSource cancellationTokenSource)
    {
        Throw.IfNull(cancellationTokenSource);

        cancellationTokenSource.Cancel();
        return Task.CompletedTask;
    }
}
#endif