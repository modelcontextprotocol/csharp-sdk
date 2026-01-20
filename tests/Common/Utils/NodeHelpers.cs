using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Helper utilities for Node.js and npm operations.
/// </summary>
public static class NodeHelpers
{
    /// <summary>
    /// Creates a ProcessStartInfo configured to run npx with the specified arguments.
    /// </summary>
    /// <param name="arguments">The arguments to pass to npx.</param>
    /// <returns>A configured ProcessStartInfo for running npx.</returns>
    public static ProcessStartInfo NpxStartInfo(string arguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, npx is a PowerShell script, so we need to use cmd.exe to invoke it
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c npx {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            // On Unix-like systems, npx is typically a shell script that can be executed directly
            return new ProcessStartInfo
            {
                FileName = "npx",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }

    /// <summary>
    /// Checks if Node.js and npx are installed and available on the system.
    /// </summary>
    /// <returns>True if npx is available, false otherwise.</returns>
    public static bool IsNpxInstalled()
    {
        try
        {
            var startInfo = NpxStartInfo("--version");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
