using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Utils;

/// <summary>
/// Helper class for working with processes.
/// </summary>
internal static class ProcessHelper
{
    private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Kills a process and all of its child processes (entire process tree).
    /// </summary>
    /// <param name="process">The process to terminate along with its child processes.</param>
    /// <remarks>
    /// This method uses a default timeout of 30 seconds when waiting for processes to exit.
    /// On Windows, this uses the "taskkill" command with the /T flag.
    /// On non-Windows platforms, it recursively identifies and terminates child processes.
    /// </remarks>
    public static void KillTree(this Process process) => process.KillTree(_defaultTimeout);

    /// <summary>
    /// Kills a process and all of its child processes (entire process tree) with a specified timeout.
    /// </summary>
    /// <param name="process">The process to terminate along with its child processes.</param>
    /// <param name="timeout">The maximum time to wait for the processes to exit.</param>
    /// <remarks>
    /// On Windows, this uses the "taskkill" command with the /T flag to terminate the process tree.
    /// On non-Windows platforms, it recursively identifies and terminates child processes.
    /// The method waits for the specified timeout for processes to exit before continuing.
    /// This is particularly useful for applications that spawn child processes (like Node.js)
    /// that wouldn't be terminated automatically when the parent process exits.
    /// </remarks>
    public static void KillTree(this Process process, TimeSpan timeout)
    {
        var pid = process.Id;
        if (_isWindows)
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

        // wait until the process finishes exiting/getting killed. 
        // We don't want to wait forever here because the task is already supposed to be dieing, we just want to give it long enough
        // to try and flush what it can and stop. If it cannot do that in a reasonable time frame then we will just ignore it.
        process.WaitForExit((int)timeout.TotalMilliseconds);
    }

    private static void GetAllChildIdsUnix(int parentId, ISet<int> children, TimeSpan timeout)
    {
        var exitcode = RunProcessAndWaitForExit(
            "pgrep",
            $"-P {parentId}",
            timeout,
            out var stdout);

        if (exitcode == 0 && !string.IsNullOrEmpty(stdout))
        {
            using var reader = new StringReader(stdout);
            while (true)
            {
                var text = reader.ReadLine();
                if (text == null)
                    return;

                if (int.TryParse(text, out var id))
                {
                    children.Add(id);
                    // Recursively get the children
                    GetAllChildIdsUnix(id, children, timeout);
                }
            }
        }
    }

    private static void KillProcessUnix(int processId, TimeSpan timeout)
    {
        RunProcessAndWaitForExit(
            "kill",
            $"-TERM {processId}",
            timeout,
            out var _);
    }

    private static int RunProcessAndWaitForExit(string fileName, string arguments, TimeSpan timeout, out string? stdout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        stdout = null;

        var process = Process.Start(startInfo);
        if (process == null)
            return -1;

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
}
