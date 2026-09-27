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
    /// The method waits for the specified timeout for the process and its descendants to exit before continuing.
    /// This is particularly useful for applications that spawn child processes (like Node.js)
    /// that wouldn't be terminated automatically when the parent process exits.
    /// </remarks>
    public static void KillTree(this Process process, TimeSpan timeout)
    {
        // Snapshot the descendants before killing anything, as the parent/child links needed to find
        // them are gone once the tree has been torn down. On Windows the root is usually the cmd.exe /c
        // wrapper, so the actual server is one of these descendants and waiting on the root alone isn't
        // enough to know that the server has released its working directory and files.
        List<Process> descendants = GetDescendantProcesses(process);
        try
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

            // wait until the processes finish exiting/getting killed.
            // We don't want to wait forever here because the tree is already supposed to be dying, we just want to give it long enough
            // to try and flush what it can and stop. If it cannot do that in a reasonable time frame then we will just ignore it.
            // The root and all of its descendants share the same timeout.
            Stopwatch stopwatch = Stopwatch.StartNew();
            process.WaitForExit(GetRemainingMilliseconds(timeout, stopwatch));
            foreach (Process descendant in descendants)
            {
                try
                {
                    descendant.WaitForExit(GetRemainingMilliseconds(timeout, stopwatch));
                }
                catch
                {
                    // The process can no longer be waited on, e.g. because it has already exited.
                }
            }
        }
        finally
        {
            foreach (Process descendant in descendants)
            {
                descendant.Dispose();
            }
        }
    }

    private static int GetRemainingMilliseconds(TimeSpan timeout, Stopwatch stopwatch) =>
        timeout == Timeout.InfiniteTimeSpan ? Timeout.Infinite :
        (int)Math.Max(0, (timeout - stopwatch.Elapsed).TotalMilliseconds);

    /// <summary>
    /// Gets all currently running descendants of <paramref name="root"/>, or as many of them as can be found.
    /// </summary>
    private static List<Process> GetDescendantProcesses(Process root)
    {
        List<Process> descendants = [];
        try
        {
            Func<int, IEnumerable<int>> getChildIds;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Both platforms list every process along with its parent ID in one pass.
                ILookup<int, int> childIds =
                    (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetParentIdsWindows() : GetParentIdsLinux())
                    .ToLookup(p => p.ParentId, p => p.Id);
                getChildIds = id => childIds[id];
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                getChildIds = GetChildIdsMacOS;
            }
            else
            {
                return descendants;
            }

            HashSet<int> visited = [root.Id];
            Queue<Process> parents = new();
            parents.Enqueue(root);
            while (parents.Count > 0)
            {
                Process parent = parents.Dequeue();
                foreach (int childId in getChildIds(parent.Id))
                {
                    if (visited.Add(childId) && TryGetChildProcess(parent, childId) is { } child)
                    {
                        descendants.Add(child);
                        parents.Enqueue(child);
                    }
                }
            }
        }
        catch
        {
            // Finding descendants is best effort. Whatever was found so far will still be waited on.
        }

        return descendants;
    }

    private static Process? TryGetChildProcess(Process parent, int childId)
    {
        Process? child = null;
        try
        {
            child = Process.GetProcessById(childId);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Open and cache a handle to the process so that later operations, such as waiting for it to exit,
                // are bound to this process even if it exits and its ID is reused.
                try
                {
                    _ = child.SafeHandle;
                }
                catch
                {
                }
            }

            // A process that started before its supposed parent can't be its child. Its parent ID is stale,
            // because the real parent has exited and the ID has since been reused.
            if (child.StartTime >= parent.StartTime)
            {
                return child;
            }
        }
        catch
        {
            // The process has already exited or can't be inspected.
        }

        child?.Dispose();
        return null;
    }

    private static IEnumerable<(int Id, int ParentId)> GetParentIdsWindows()
    {
        List<(int Id, int ParentId)> processes = [];

        nint snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return processes;
        }

        try
        {
            PROCESSENTRY32W entry = default;
            unsafe
            {
                entry.dwSize = (uint)sizeof(PROCESSENTRY32W);
            }

            for (int found = Process32FirstW(snapshot, ref entry); found != 0; found = Process32NextW(snapshot, ref entry))
            {
                processes.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
            }
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }

        return processes;
    }

    private static IEnumerable<(int Id, int ParentId)> GetParentIdsLinux()
    {
        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int id))
            {
                continue;
            }

            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(directory, "stat"));
            }
            catch
            {
                // The process has exited since the directory was enumerated.
                continue;
            }

            // The format is "pid (comm) state ppid ...". comm can contain spaces and parentheses, so parse from the last ')'.
            int commEnd = stat.LastIndexOf(')');
            if (commEnd < 0)
            {
                continue;
            }

            string[] fields = stat.Substring(commEnd + 1).Split([' '], StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 1 && int.TryParse(fields[1], out int parentId))
            {
                yield return (id, parentId);
            }
        }
    }

    private static unsafe IEnumerable<int> GetChildIdsMacOS(int parentId)
    {
        // Calling with no buffer returns an upper bound for the number of children.
        int count = proc_listchildpids(parentId, null, 0);
        if (count <= 0)
        {
            return [];
        }

        int[] ids = new int[count];
        fixed (int* pIds = ids)
        {
            count = proc_listchildpids(parentId, pIds, ids.Length * sizeof(int));
        }

        return ids.Take(count);
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const nint INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32FirstW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int Process32NextW(nint hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CloseHandle(nint hObject);

    [DllImport("libproc")]
    private static extern unsafe int proc_listchildpids(int ppid, int* buffer, int buffersize);

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
