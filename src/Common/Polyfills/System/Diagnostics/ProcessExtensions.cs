// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Diagnostics;

internal static class ProcessExtensions
{
    public static void Kill(this Process process, bool entireProcessTree)
    {
        _ = entireProcessTree;
        process.Kill();
    }

    public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
    {
        if (process.HasExited)
        {
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        void ProcessExitedHandler(object? sender, EventArgs e) => tcs.TrySetResult(true);

        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += ProcessExitedHandler;

            using var _ = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            process.Exited -= ProcessExitedHandler;
        }
    }
}