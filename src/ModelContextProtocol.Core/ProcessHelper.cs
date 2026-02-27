using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol;

/// <summary>
/// Helper class for working with processes.
/// </summary>
internal static class ProcessHelper
{
    /// <summary>
    /// Kills a process and all of its child processes (entire process tree) with a specified timeout.
    /// </summary>
    /// <param name="process">The process to terminate along with its child processes.</param>
    /// <param name="timeout">The maximum time to wait for the processes to exit.</param>
    /// <remarks>
    /// On .NET Core 3.0+ this uses <c>Process.Kill(entireProcessTree: true)</c>.
    /// On .NET Standard 2.0, it uses platform-specific commands (taskkill on Windows, pgrep/kill on Unix).
    /// The method waits for the specified timeout for processes to exit before continuing.
    /// This is particularly useful for applications that spawn child processes (like Node.js)
    /// that wouldn't be terminated automatically when the parent process exits.
    /// </remarks>
    public static void KillTree(this Process process, TimeSpan timeout)
    {
#if NETSTANDARD2_0
        // Process.Kill(entireProcessTree) is not available on .NET Standard 2.0.
        // Use platform-specific commands to kill the process tree.
        var pid = process.Id;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            RunProcessAndWaitForExit(
                "taskkill",
                $"/T /F /PID {pid}",
                timeout,
                out var _);
        }
        else
        {
            var children = new HashSet<int>();
            GetAllChildIdsUnix(pid, children, timeout);
            foreach (var childId in children)
            {
                KillProcessUnix(childId, timeout);
            }

            KillProcessUnix(pid, timeout);
        }
#else
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process has already exited
            return;
        }
#endif

        // wait until the process finishes exiting/getting killed. 
        // We don't want to wait forever here because the task is already supposed to be dying, we just want to give it long enough
        // to try and flush what it can and stop. If it cannot do that in a reasonable time frame then we will just ignore it.
        process.WaitForExit((int)timeout.TotalMilliseconds);
    }

#if NETSTANDARD2_0
    private static void GetAllChildIdsUnix(int parentId, ISet<int> children, TimeSpan timeout)
    {
        int exitcode = RunProcessAndWaitForExit("pgrep", $"-P {parentId}", timeout, out var stdout);

        if (exitcode == 0 && !string.IsNullOrEmpty(stdout))
        {
            using var reader = new StringReader(stdout);
            while (reader.ReadLine() is string text)
            {
                if (int.TryParse(text, out var id))
                {
                    children.Add(id);

                    // Recursively get the children
                    GetAllChildIdsUnix(id, children, timeout);
                }
            }
        }
    }

    private static void KillProcessUnix(int processId, TimeSpan timeout) =>
        RunProcessAndWaitForExit("kill", $"-TERM {processId}", timeout, out _);

    private static int RunProcessAndWaitForExit(string fileName, string arguments, TimeSpan timeout, out string? stdout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        stdout = null;

        if (Process.Start(startInfo) is { } process)
        {
            if (process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                stdout = process.StandardOutput.ReadToEnd();
            }
            else
            {
                process.Kill();
            }

            return process.ExitCode;
        }

        return -1;
    }
#endif
}
