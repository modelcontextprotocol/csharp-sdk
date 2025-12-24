using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Utils;

internal static class ProcessStartInfoUtilities
{
    private static bool IsWindows =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    public static ProcessStartInfo CreateOnPath(
        string fileName,
        string? arguments = null,
        bool redirectStandardInput = false,
        bool redirectStandardOutput = true,
        bool redirectStandardError = true,
        bool useShellExecute = false,
        bool createNoWindow = true)
    {
        string resolved = FindOnPath(fileName) ?? throw new InvalidOperationException($"{fileName} was not found on PATH.");

        if (IsWindows && !useShellExecute &&
            (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            // Batch files require cmd.exe when UseShellExecute=false.
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /s /c \"\"{resolved}\" {arguments ?? string.Empty}\"",
                RedirectStandardInput = redirectStandardInput,
                RedirectStandardOutput = redirectStandardOutput,
                RedirectStandardError = redirectStandardError,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
            };
        }

        return new ProcessStartInfo
        {
            FileName = resolved,
            Arguments = arguments ?? string.Empty,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = redirectStandardError,
            UseShellExecute = useShellExecute,
            CreateNoWindow = createNoWindow,
        };
    }

    public static string? FindOnPath(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        string[] extensions;
        if (IsWindows)
        {
            // Match cmd.exe resolution semantics by honoring PATHEXT.
            string? pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrWhiteSpace(pathext))
            {
                extensions = [".EXE", ".CMD", ".BAT"];
            }
            else
            {
                string[] raw = pathext.Split(';');
                var list = new List<string>(raw.Length);
                foreach (string ext in raw)
                {
                    string trimmed = ext.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        list.Add(trimmed);
                    }
                }

                extensions = list.ToArray();
            }
        }
        else
        {
            extensions = [];
        }

        bool hasExtension = Path.HasExtension(fileName);

        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            string trimmedDir = dir.Trim().Trim('"');

            if (!IsWindows || hasExtension)
            {
                string fullPath = Path.Combine(trimmedDir, fileName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }

                continue;
            }

            foreach (string ext in extensions)
            {
                string fullPath = Path.Combine(trimmedDir, fileName + ext);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            // Also consider no-extension in case it exists directly on disk.
            string noExtPath = Path.Combine(trimmedDir, fileName);
            if (File.Exists(noExtPath))
            {
                return noExtPath;
            }
        }

        return null;
    }
}
